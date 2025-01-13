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
    private string mock =
        "{\"forecasts\":[{\"pv_estimate\":0.7781,\"pv_estimate10\":0.3988,\"pv_estimate90\":1.7858,\"period_end\":\"2025-01-13T15:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.2401,\"pv_estimate10\":0.1547,\"pv_estimate90\":1.318,\"period_end\":\"2025-01-13T15:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.0917,\"pv_estimate10\":0.0539,\"pv_estimate90\":0.1294,\"period_end\":\"2025-01-13T16:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.0055,\"pv_estimate10\":0.0055,\"pv_estimate90\":0.0055,\"period_end\":\"2025-01-13T16:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T17:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T17:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T18:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T18:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T19:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T19:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T20:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T20:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T21:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T21:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T22:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T22:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T23:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-13T23:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T00:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T00:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T01:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T01:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T02:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T02:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T03:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T03:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T04:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T04:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T05:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T05:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T06:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T06:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T07:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T07:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T08:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.027,\"pv_estimate10\":0.0162,\"pv_estimate90\":0.0402,\"period_end\":\"2025-01-14T08:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.1672,\"pv_estimate10\":0.0863,\"pv_estimate90\":0.2689,\"period_end\":\"2025-01-14T09:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.4016,\"pv_estimate10\":0.1654,\"pv_estimate90\":0.9193,\"period_end\":\"2025-01-14T09:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.8432,\"pv_estimate10\":0.2561,\"pv_estimate90\":1.6017,\"period_end\":\"2025-01-14T10:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":1.1581,\"pv_estimate10\":0.3167,\"pv_estimate90\":2.3624,\"period_end\":\"2025-01-14T10:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":1.1667,\"pv_estimate10\":0.3049,\"pv_estimate90\":3.0136,\"period_end\":\"2025-01-14T11:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":1.2541,\"pv_estimate10\":0.3018,\"pv_estimate90\":3.5867,\"period_end\":\"2025-01-14T11:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":1.5276,\"pv_estimate10\":0.3452,\"pv_estimate90\":3.9844,\"period_end\":\"2025-01-14T12:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":1.7781,\"pv_estimate10\":0.3679,\"pv_estimate90\":4.2895,\"period_end\":\"2025-01-14T12:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":2.0035,\"pv_estimate10\":0.3765,\"pv_estimate90\":4.4182,\"period_end\":\"2025-01-14T13:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":1.853,\"pv_estimate10\":0.2988,\"pv_estimate90\":4.4198,\"period_end\":\"2025-01-14T13:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":1.2849,\"pv_estimate10\":0.2149,\"pv_estimate90\":4.3429,\"period_end\":\"2025-01-14T14:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.6769,\"pv_estimate10\":0.1433,\"pv_estimate90\":4.0058,\"period_end\":\"2025-01-14T14:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.4024,\"pv_estimate10\":0.0921,\"pv_estimate90\":3.3162,\"period_end\":\"2025-01-14T15:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.2275,\"pv_estimate10\":0.0569,\"pv_estimate90\":2.4772,\"period_end\":\"2025-01-14T15:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.1086,\"pv_estimate10\":0.031,\"pv_estimate90\":1.1674,\"period_end\":\"2025-01-14T16:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.0103,\"pv_estimate10\":0,\"pv_estimate90\":0.0103,\"period_end\":\"2025-01-14T16:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T17:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T17:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T18:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T18:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T19:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T19:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T20:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T20:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T21:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T21:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T22:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T22:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T23:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-14T23:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T00:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T00:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T01:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T01:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T02:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T02:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T03:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T03:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T04:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T04:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T05:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T05:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T06:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T06:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T07:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T07:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0,\"pv_estimate10\":0,\"pv_estimate90\":0,\"period_end\":\"2025-01-15T08:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.0205,\"pv_estimate10\":0.0051,\"pv_estimate90\":0.0402,\"period_end\":\"2025-01-15T08:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.1126,\"pv_estimate10\":0.0205,\"pv_estimate90\":0.286,\"period_end\":\"2025-01-15T09:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.2251,\"pv_estimate10\":0.0461,\"pv_estimate90\":0.939,\"period_end\":\"2025-01-15T09:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.4196,\"pv_estimate10\":0.0819,\"pv_estimate90\":1.6689,\"period_end\":\"2025-01-15T10:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.5616,\"pv_estimate10\":0.0912,\"pv_estimate90\":2.2341,\"period_end\":\"2025-01-15T10:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.5747,\"pv_estimate10\":0.0652,\"pv_estimate90\":2.7364,\"period_end\":\"2025-01-15T11:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.6256,\"pv_estimate10\":0.0551,\"pv_estimate90\":3.3546,\"period_end\":\"2025-01-15T11:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.744,\"pv_estimate10\":0.0752,\"pv_estimate90\":3.932,\"period_end\":\"2025-01-15T12:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.8066,\"pv_estimate10\":0.0943,\"pv_estimate90\":4.3674,\"period_end\":\"2025-01-15T12:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.8233,\"pv_estimate10\":0.0993,\"pv_estimate90\":4.495,\"period_end\":\"2025-01-15T13:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.7814,\"pv_estimate10\":0.0993,\"pv_estimate90\":4.5509,\"period_end\":\"2025-01-15T13:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.6805,\"pv_estimate10\":0.0893,\"pv_estimate90\":4.3245,\"period_end\":\"2025-01-15T14:00:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.5285,\"pv_estimate10\":0.0745,\"pv_estimate90\":4.1915,\"period_end\":\"2025-01-15T14:30:00.0000000Z\",\"period\":\"PT30M\"},{\"pv_estimate\":0.3585,\"pv_estimate10\":0.0546,\"pv_estimate90\":3.5194,\"period_end\":\"2025-01-15T15:00:00.0000000Z\",\"period\":\"PT30M\"}]}";

    public async Task<IEnumerable<SolcastForecast>> GetSolcastForecast()
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
            SolcastResponse? newForecast = null;

            if (System.Diagnostics.Debugger.IsAttached)
            {
                newForecast = JsonSerializer.Deserialize<SolcastResponse>(mock);
            }
            else
            {
                newForecast = await url.GetJsonAsync<SolcastResponse>();
            }

            if (newForecast != null && newForecast.forecasts != null && newForecast.forecasts.Any())
            {
                return newForecast.forecasts;
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