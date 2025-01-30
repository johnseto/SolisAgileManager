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
public class SolcastAPI( SolisManagerConfig config, ILogger<SolcastAPI> logger )
{
    private IEnumerable<SolarForecast>? lastForecastData;
    private DateTime? lastAPIUpdate = null;

    private string GetDiskCachePath(string siteId) => Path.Combine(Program.ConfigFolder, $"Solcast-raw-{siteId}.json");
    
    private async Task CacheSolcastDataToDisk(string siteId, SolcastResponse response)
    {
        var file = GetDiskCachePath(siteId);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(file, json);
    }

    private async Task<SolcastResponse?> ReadCachedSolcastDataFromDisk(string siteId)
    {
        var file = GetDiskCachePath(siteId);

        if (!File.Exists(file))
            return null;

        var json = await File.ReadAllTextAsync(file);
        logger.LogInformation("Loaded cached Solcast data from {F}", file);
        return JsonSerializer.Deserialize<SolcastResponse>(json);
    }
    
    private async Task<SolcastResponse?> GetSolcastForecast(string siteIdentifier, bool useDiskCache)
    {
        if (useDiskCache)
        {
            var response = await ReadCachedSolcastDataFromDisk(siteIdentifier);
            if( response != null)
                return response;
        }

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
                await CacheSolcastDataToDisk(siteIdentifier, responseData);
                return responseData;
            }
        }
        catch (FlurlHttpException ex)
        {
            if (ex.StatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning(
                    "Solcast API failed - too many requests. Will try again at next scheduled update");

                // If it's a 429 and we were told not to use the disk cache, use it anyway
                if( ! useDiskCache )
                    return await ReadCachedSolcastDataFromDisk(siteIdentifier);
            }
            else
                logger.LogError("HTTP Exception getting solcast data: {E}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError("Exception getting solcast data: {E}", ex);
        }

        return null;
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
    
    public async Task UpdateSolcastDataFromAPI(bool useDiskCache, bool overwrite)
    {
        if (!config.SolcastValid())
            return;

        Dictionary<DateTime, SolarForecast> data = new();

        var siteIdentifiers = GetSolcastSites(config.SolcastSiteIdentifier);

        if(siteIdentifiers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != siteIdentifiers.Length)
            logger.LogWarning("Same Solcast site ID specified twice in config. Ignoring the second one");

        // Only ever take the first 2
        foreach (var siteIdentifier in siteIdentifiers.Take(2))
        {
            // Hit the actual Solcast API (or use the cached data on disk)
            var responseData = await GetSolcastForecast(siteIdentifier, useDiskCache);

            if (responseData is { forecasts: not null })
            {
                logger.LogInformation("{C} Solcast forecasts returned for site ID {ID}", responseData.forecasts.Count(), siteIdentifier);
                
                foreach (var x in responseData.forecasts)
                {
                    var start = x.period_end.AddMinutes(-30);
                    
                    if (!data.TryGetValue(start, out var forecast))
                    {
                        forecast = new SolarForecast { PeriodStart = start };
                        data[start] = forecast;
                    }
                    
                    // Divide the kW figure by 2 to get the power
                    forecast.ForecastkWh += x.pv_estimate / 2.0M;
                }
            }
        }

        if (data.Values.Count != 0)
        {
            if (lastForecastData != null && !overwrite)
            {
                // Call TryAdd, which will add any that don't already exist
                var count = lastForecastData.Count(forecast => data.TryAdd(forecast.PeriodStart, forecast));
                logger.LogInformation("Merged new forecasts with {C} existing ones", count);
            }

            lastAPIUpdate = DateTime.UtcNow;
            lastForecastData = data.Values.OrderBy(x => x.PeriodStart).ToList();
        }
    }

    
    public async Task<(IEnumerable<SolarForecast>? forecasts, DateTime? lastApiUpdate)> GetSolcastForecast()
    {
        try
        {
            if (lastForecastData is null || !lastForecastData.Any())
            {
                await UpdateSolcastDataFromAPI(true, false);
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

    
    private record SolcastResponse
    {
        public DateTime lastUpdate { get; } = DateTime.UtcNow;
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