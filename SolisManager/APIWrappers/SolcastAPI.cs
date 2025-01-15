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
    public async Task<IEnumerable<SolarForecast>> GetSolcastForecast()
    {
        logger.LogInformation("Attempting to pull forecast data from Solcast API...");
        
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
            var filename = Path.Combine(Program.ConfigFolder, $"Solcast-{DateTime.UtcNow:dd-MMM-yyyy}.json");
            SolcastResponse? newForecast = null;

            if (System.Diagnostics.Debugger.IsAttached && File.Exists(filename))
            {
                var json = await File.ReadAllTextAsync(filename);
                newForecast = JsonSerializer.Deserialize<SolcastResponse>(json);
            }
            else
            {
                newForecast = await url.GetJsonAsync<SolcastResponse>();
            }

            if (newForecast != null && newForecast.forecasts != null && newForecast.forecasts.Any())
            {
                logger.LogInformation("Retrieved {N} new forecasts from Solcast", newForecast.forecasts.Count());

                if (System.Diagnostics.Debugger.IsAttached)
                {
                    // Minimise Solcast API calls when debugging by writing the result of the call
                    // to JSON and then reading it for future calls on the same day.
                    var json = JsonSerializer.Serialize(newForecast, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(filename, json);
                }
                return CreateForecasts(newForecast.forecasts);
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

        return [];
    }

    private IEnumerable<SolarForecast> CreateForecasts(IEnumerable<SolcastForecast> forecasts)
    {
        return forecasts.Select(x => new SolarForecast
        {
            ForecastkWh = x.pv_estimate / 2, // 30 min slot, so divide kW by 2 to get kWh
            PeriodStart = x.period_end.AddMinutes(-30)
        });
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
        public string period { get; set; }

        public override string ToString()
        {
            return $"{period_end} - {pv_estimate}kWh";
        }
    }
}