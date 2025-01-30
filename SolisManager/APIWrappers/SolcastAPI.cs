using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Flurl;
using Flurl.Http;
using SolisManager.Shared.Models;
using static SolisManager.Extensions.EnergyExtensions;
namespace SolisManager.APIWrappers;

/// <summary>
/// https://docs.solcast.com.au/#api-authentication
/// https://docs.solcast.com.au/#155071c9-3457-47ea-a689-88fa894b0f51
/// </summary>
public class SolcastAPI(SolisManagerConfig config, ILogger<SolcastAPI> logger)
{
    private IEnumerable<SolarForecast>? lastForecastData;
    private DateTime? lastAPIUpdate = null;
    private SolcastResponseCache? responseCache = null;

    private string DiskCachePath => Path.Combine(Program.ConfigFolder, $"Solcast-cache.json");
    
    private async Task LoadCachedSolcastDataFromDisk(string siteId)
    {
        if (responseCache == null)
        {
            var file = DiskCachePath;

            if (File.Exists(file))
            {
                var json = await File.ReadAllTextAsync(file);
                logger.LogInformation("Loaded cached Solcast data from {F}", file);
                
                responseCache = JsonSerializer.Deserialize<SolcastResponseCache>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
                if (responseCache == null || responseCache.date != today)
                {
                    // New day, discard the old data
                    responseCache = new SolcastResponseCache{ date = today };
                }
            }

        }

        responseCache ??= new();
    }

    private async Task CacheSolcastResponse(string siteId, SolcastResponse response)
    {
        ArgumentNullException.ThrowIfNull(responseCache);
        
        var site = responseCache.sites.FirstOrDefault(x => x.siteId == siteId);
        if (site == null)
        {
            site = new SolcastResponseCacheEntry { siteId = siteId };
            responseCache.sites.Add(site);
        }

        if (!site.updates.Exists(x => x.lastUpdate == response.lastUpdate))
        {
            // Save the response
            site.updates.Add(response);
            // Update the date
            responseCache.date = DateOnly.FromDateTime(response.lastUpdate);
        }

        if (site.updates.Count > 3)
            logger.LogError("Unexpected response count of {C}", site.updates.Count);

        var json = JsonSerializer.Serialize(responseCache, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(DiskCachePath, json);
    }
    
    public async Task GetNewSolcastForecasts()
    {
        var siteIdentifiers = GetSolcastSites(config.SolcastSiteIdentifier);

        if (siteIdentifiers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != siteIdentifiers.Length)
            logger.LogWarning("Same Solcast site ID specified twice in config. Ignoring the second one");

        // Only ever take the first 2
        foreach (var siteIdentifier in siteIdentifiers.Take(2))
        {
            // Use WhenAll here?
            await GetNewSolcastForecast(siteIdentifier);
        }
    }

    private async Task ReadLegacySolcastFile(string siteId)
    {
        var file = Path.Combine(Program.ConfigFolder, $"Solcast-raw-{siteId}.json");
        if (File.Exists(file))
        {
            var json = await File.ReadAllTextAsync(file);
            var response = JsonSerializer.Deserialize<SolcastResponse>(json);
            if (response != null)
            {
                response.lastUpdate = File.GetLastWriteTimeUtc(file);
                await CacheSolcastResponse(siteId, response);
            }
        }
    }
    
    private async Task GetNewSolcastForecast(string siteIdentifier)
    {
        // First, check we've got the cache initialised
        await LoadCachedSolcastDataFromDisk(siteIdentifier);
        
        var url = "https://api.solcast.com.au"
            .AppendPathSegment("rooftop_sites")
            .AppendPathSegment(siteIdentifier)
            .AppendPathSegment("forecasts")
            .SetQueryParams(new
            {
                format = "json",
                api_key = config.SolcastAPIKey
            });

        try
        {
            logger.LogInformation("Querying Solcast API for forecast (site ID: {ID})...", siteIdentifier);

            var responseData = await url.GetJsonAsync<SolcastResponse>();

            if (responseData != null)
            {
                // We got one. Add it to the cache
                await CacheSolcastResponse(siteIdentifier, responseData);
            }
        }
        catch (FlurlHttpException ex)
        {
            if (ex.StatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                await ReadLegacySolcastFile(siteIdentifier);
                
                logger.LogWarning(
                    "Solcast API failed - too many requests. Will try again at next scheduled update");
            }
            else
                logger.LogError("HTTP Exception getting solcast data: {E}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError("Exception getting solcast data: {E}", ex);
        }
    }

    public static string[] GetSolcastSites(string siteIdList)
    {
        string[] siteIdentifiers;

        // We support up to 2 site IDs for people with multiple strings
        if (siteIdList.Contains(','))
            siteIdentifiers = siteIdList.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        else
            siteIdentifiers = [siteIdList];

        return siteIdentifiers;
    }

    public void UpdateSolcastDataFromAPI()
    {
        if (!config.SolcastValid() || responseCache == null)
            return;

        Dictionary<DateTime, decimal> data = new();

        foreach (var site in responseCache.sites)
        {
            var siteData = AggregateSiteData(site);

            // Add it to the overall total
            foreach (var pair in siteData)
            {
                if( ! data.ContainsKey(pair.Start))
                    data[pair.Start] = pair.energy;
                else
                    data[pair.Start] += pair.energy;
            }
        }

        if (data.Values.Count != 0)
        {
            lastForecastData = data.Select( x => new SolarForecast
            {  
                PeriodStart = x.Key, 
                ForecastkWh = x.Value
            }).OrderBy(x => x.PeriodStart)
                .ToList();
        }
    }

    private List<(DateTime Start, decimal energy)> AggregateSiteData(SolcastResponseCacheEntry siteData)
    {
        Dictionary<DateTime, decimal> data = new();

        foreach (var update in siteData.updates)
        {
            if (update.forecasts == null)
                continue;
            
            lastAPIUpdate = update.lastUpdate;

            foreach (var datapoint in update.forecasts)
            {
                var start = datapoint.period_end.AddMinutes(-30);
                
                // No idea why, but it seems that the pv_estimate is about twice
                // what it should be, all the time. So half it. Maybe we can
                // figure out why sometime in future. :)
                const decimal mysteryFactor = 0.5M;

                // Divide the kW figure by 2 to get the power, and save into 
                // the dict, overwriting anything that came before.
                data[start] = (datapoint.pv_estimate / 2.0M) * mysteryFactor;
            }
        }
        
        return data.Select(x => (x.Key, x.Value))
            .OrderBy(x => x.Key)
            .ToList();
    }

    public (IEnumerable<SolarForecast>? forecasts, DateTime? lastApiUpdate) GetSolcastForecast()
    {
        try
        {
            if (lastForecastData is null || !lastForecastData.Any())
            { 
                UpdateSolcastDataFromAPI();
            }

            return (lastForecastData, lastAPIUpdate);
        }
        catch (FlurlHttpException ex)
        {
            if (ex.StatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("Solcast API failed - too many requests. Will try again at next scheduled update");
            }
            else
                logger.LogError("HTTP Exception getting solcast data: {E}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError("Exception getting solcast data: {E}", ex);
        }

        return (null, null);
    }
    
    private record SolcastResponseCache
    {
        public DateOnly? date { get; set; }
        public List<SolcastResponseCacheEntry> sites { get; init; } = [];
    };

    private record SolcastResponseCacheEntry
    {
        public string siteId { get; set; } = string.Empty;
        public List<SolcastResponse> updates { get; init; } = [];
    };

    private record SolcastResponse
    {
        public DateTime lastUpdate { get; set; } = DateTime.UtcNow;
        public IEnumerable<SolcastForecast>? forecasts { get; set; } = [];
    }
    
    private record SolcastForecast
    {
        public decimal pv_estimate { get; set;  }
        public decimal pv_estimate10 { get; set;  }
        public decimal pv_estimate90 { get; set;  }
        public DateTime period_end { get; set; }

        public override string ToString()
        {
            return $"{period_end} = {pv_estimate}kW";
        }
    }
}