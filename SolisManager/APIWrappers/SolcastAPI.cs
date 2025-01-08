using System.Net;
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
    private SolcastResponse? lastForecast = null;
    public async Task<IEnumerable<SolcastForecast>> GetSolcastForecast()
    {
        if (lastForecast == null || DateTime.UtcNow - lastForecast.lastUpdate < TimeSpan.FromHours(6))
        {
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
                lastForecast = await url.GetJsonAsync<SolcastResponse>();
            }
            catch (FlurlHttpException ex)
            {
                if (ex.StatusCode == (int)HttpStatusCode.TooManyRequests)
                {
                    logger.LogWarning("Solcast API failed - too many requests. Will try again later");
                    lastForecast = new SolcastResponse();
                }
                else
                    logger.LogError("HTTP Exception getting solcast data: {E}", ex);
            }
            catch (Exception ex)
            {
                logger.LogError("Exception getting solcast data: {E}", ex);
            }
        }

        if (lastForecast != null && lastForecast.forecasts != null && lastForecast.forecasts.Any())
        {
            return lastForecast.forecasts;
        }

        return [];
    }

    private record SolcastResponse
    {
        public DateTime lastUpdate { get; } = DateTime.UtcNow;
        public IEnumerable<SolcastForecast>? forecasts { get; set; } = [];
    }
    
    public record SolcastForecast
    {
        public decimal pv_estimate { get; set;  }
        public decimal pv_estimate10 { get; set;  }
        public decimal pv_estimate90 { get; set;  }
        public DateTime period_end { get; set; }
        public string period { get; set; }
    }
}