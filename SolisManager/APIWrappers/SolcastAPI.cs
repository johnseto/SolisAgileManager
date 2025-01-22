using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Flurl;
using Flurl.Http;
using SolisManager.Shared.Models;

namespace SolisManager.APIWrappers;

/// <summary>
/// https://docs.solcast.com.au/#api-authentication
/// https://docs.solcast.com.au/#155071c9-3457-47ea-a689-88fa894b0f51
/// </summary>
public class SolcastAPI( SolisManagerConfig config, ILogger<SolcastAPI> logger )
{
    private IEnumerable<SolarForecast>? lastForecastData;
    private DateTime? lastAPIUpdate = null;
    
    public async Task UpdateSolcastDataFromAPI()
    {
        if (!config.SolcastValid())
            return;
        
        logger.LogInformation("Executing scheduled update from Solcast API...");
        
        var url = "https://api.solcast.com.au"
            .AppendPathSegment("rooftop_sites")
            .AppendPathSegment(config.SolcastSiteIdentifier)
            .AppendPathSegment("forecasts")
            .SetQueryParams(new
            {
                format = "json",
                api_key = config.SolcastAPIKey
            });

        try
        {
            var responseData = await url.GetJsonAsync<SolcastResponse>();

            if (responseData is { forecasts: not null })
            {
                lastAPIUpdate = DateTime.UtcNow;
                
                lastForecastData = responseData.forecasts.Select(x => new SolarForecast
                {
                    ForecastkWh = x.pv_estimate / 2, // 30 min slot, so divide kW by 2 to get kWh
                    PeriodStart = x.period_end.AddMinutes(-30)
                });

                // And cache it locally
                await WriteSolcastDataToFile();
            }
        }
        catch (FlurlHttpException ex)
        {
            if (ex.StatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("Solcast API failed - too many requests. Will try again later");
            }
            else
                logger.LogError("HTTP Exception getting solcast data: {E}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError("Exception getting solcast data: {E}", ex);
        }
    }

    private async Task WriteSolcastDataToFile()
    {
        if (lastForecastData != null && lastForecastData.Any())
        {
            var file = LocalCacheFileName();
            var json = JsonSerializer.Serialize(lastForecastData, new JsonSerializerOptions { WriteIndented = true });

            logger.LogInformation("Retrieved {N} forecasts from Solcast API. Data will be cached to {F} to reduce API calls",
                lastForecastData.Count(), file);
            await File.WriteAllTextAsync(file, json);
        }
    }

    private async Task<bool> LoadSolcastDataFromFile()
    {
        var file = LocalCacheFileName();

        if (File.Exists(file))
        {
            var json = await File.ReadAllTextAsync(file);
            var fileForecasts = JsonSerializer.Deserialize<IEnumerable<SolarForecast>>(json);
            if (fileForecasts is not null)
            {
                logger.LogInformation("Loaded {N} forecasts from {F} to reduce API calls", fileForecasts.Count(), file);
                lastForecastData = fileForecasts;
                return true;
            }
        }

        return false;
    }

    private string LocalCacheFileName()
    {
        var cacheFile = Debugger.IsAttached ? $"Solcast-{DateTime.UtcNow:dd-MMM-yyyy}.json" : "Solcast-latest.json";
        return Path.Combine(Program.ConfigFolder, cacheFile );
    }

    public async Task<(IEnumerable<SolarForecast>? forecasts, DateTime? lastApiUpdate)> GetSolcastForecast()
    {
        try
        {
            if (lastForecastData is null || !lastForecastData.Any())
            {
                if (!await LoadSolcastDataFromFile())
                {
                    await UpdateSolcastDataFromAPI();
                }
            }

            return (lastForecastData, lastAPIUpdate);
        }
        catch (FlurlHttpException ex)
        {
            if (ex.StatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("Solcast API failed - too many requests. Will try again later");
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
            return $"{period_end} - {pv_estimate}kWh";
        }
    }
}