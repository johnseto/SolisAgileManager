
using System.Text.Json;
using Flurl;
using Flurl.Http;
using SolisManager.Shared.Models;

namespace SolisManager.APIWrappers;

public class OctopusAPI( SolisManagerConfig config, ILogger<OctopusAPI> logger)
{
    public async Task<IEnumerable<OctopusPriceSlot>> GetOctopusRates()
    {
        var from = DateTime.UtcNow;
        var to = DateTime.UtcNow.AddHours(36);

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
                // Ensure they're in date order. Sometimes they come back in random order!!!
                var orderedSlots = result.results!.OrderBy(x => x.valid_from).ToList();
            
                var first = result.results.FirstOrDefault()?.valid_from;
                var last = result.results.LastOrDefault()?.valid_to;
                logger.LogInformation("Retrieved {C} rates from Octopus ({S:dd-MMM-yyyy HH:mm} - {E:dd-MMM-yyyy HH:mm})", 
                    result.count, first, last);
                
                return orderedSlots;
            }
        }

        return [];
    }

    private async Task<string?> GetAuthToken(string apiKey)
    {
        var krakenQuery = """
                          mutation krakenTokenAuthentication($api: String!) {
                          obtainKrakenToken(input: {APIKey: $api}) {
                              token
                          }
                          }
                          """;
        var variables = new { api = apiKey };
        var payload = new { query = krakenQuery, variables = variables };

        var response = await "https://api.octopus.energy"
            .AppendPathSegment("/v1/graphql/")
            .PostJsonAsync(payload)
            .ReceiveJson<KrakenTokenResponse>();
        
        return response?.data?.obtainKrakenToken?.token;
    }

    private record KrakenToken(string token);
    private record KrakenResponse(KrakenToken obtainKrakenToken);

    private record KrakenTokenResponse(KrakenResponse data);

    public async Task<OctopusAccountDetails?> GetOctopusAccount(string apiKey, string accountNumber)
    {
        var token = await GetAuthToken(apiKey);

        // https://api.octopus.energy/v1/accounts/{number}

        var result = await "https://api.octopus.energy/"
            .WithHeader("Authorization", token)
            .AppendPathSegment($"/v1/accounts/{accountNumber}/")
            .GetJsonAsync<OctopusAccountDetails>();

        return result;
    }

    public static string GetProductFromTariffCode(string tariffCode)
    {
        if (string.IsNullOrEmpty(tariffCode))
            return string.Empty;
        
        var lastDash = tariffCode.LastIndexOf('-');
        if( lastDash > 0 )
            tariffCode = tariffCode.Substring(0, lastDash);

        var first = tariffCode.IndexOf('-');
        if (first > 0)
        {
            tariffCode = tariffCode.Substring(first + 1);
            var second = tariffCode.IndexOf('-');
            if (second > 0)
            {
                return tariffCode.Substring(second + 1);
            }
        }

        return string.Empty;
    }
    
    public async Task<string?> GetCurrentOctopusTariffCode(string apiKey, string accountNumber)
    {
        var accountDetails = await GetOctopusAccount(apiKey, accountNumber);
        
        if (accountDetails != null)
        {
            var importAgreement = accountDetails.properties?
                .First().electricity_meter_points?
                .FirstOrDefault(x => !x.is_export)?
                .agreements.FirstOrDefault(x => x.valid_from < DateTime.UtcNow && x.valid_to > DateTime.UtcNow);

            if (importAgreement != null)
            {
                logger.LogInformation("Found Octopus Product/Contract: {P}, Starts {S}", importAgreement.tariff_code, importAgreement.valid_from);
                return importAgreement.tariff_code;
            }
        }

        return null;
    }

    public record OctopusAgreement(string tariff_code, DateTime valid_from, DateTime valid_to);
    public record OctopusMeter(string serial_number);
    public record OctopusMeterPoints(string mpan, OctopusMeter[] meters, OctopusAgreement[] agreements, bool is_export);
    public record OctopusProperty(int id, OctopusMeterPoints[] electricity_meter_points);
    public record OctopusAccountDetails(string number, OctopusProperty[] properties);
    
    private record OctopusPrices(int count, OctopusPriceSlot[] results);
}