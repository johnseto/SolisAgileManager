
using Flurl;
using Flurl.Http;
using SolisManager.Shared.Models;

namespace SolisManager.APIWrappers;

public class OctopusAPI( SolisManagerConfig config, ILogger<OctopusAPI> logger)
{
    public async Task<IEnumerable<OctopusPriceSlot>> GetOctopusRates()
    {
        var from = DateTime.UtcNow;
        var to = DateTime.UtcNow.AddHours(24);

        // https://api.octopus.energy/v1/products/AGILE-24-10-01/electricity-tariffs/E-1R-AGILE-24-10-01-A/standard-unit-rates/
        
        var result = await "https://api.octopus.energy"
            .AppendPathSegment("/v1/products")
            .AppendPathSegment(config.OctopusProduct)
            .AppendPathSegment("electricity-tariffs")
            .AppendPathSegment(config.OctopusProductCode)
            .AppendPathSegment("standard-unit-rates")
            .SetQueryParams(new {
                period_from = from,
                period_to = to
            }).GetJsonAsync<OctopusPrices?>();
        
        if (result != null)
        {
            if (result.count != 0 && result.results != null)
            {
                logger.LogInformation("Retrieved {C} rates from Octopus ({S:dd-MMM-yyyy HH:mm} - {E:dd-MMM-yyyy HH:mm})", result.count,
                    result.results.FirstOrDefault()?.valid_from, result.results.LastOrDefault()?.valid_to);
                
                return result.results;
            }
        }

        return [];
    }
    
    private record OctopusPrices
    {
        public int count { get; set;  }
        public IEnumerable<OctopusPriceSlot>? results { get; set; }
    } 
}