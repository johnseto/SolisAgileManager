
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
        var to = DateTime.UtcNow.AddDays(5);

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

                return SplitToHalfHourSlots(orderedSlots);
            }
        }

        return [];
    }

    /// <summary>
    /// Keep the granularity for easy manual overrides
    /// </summary>
    /// <param name="slots"></param>
    /// <returns></returns>
    private IEnumerable<OctopusPriceSlot> SplitToHalfHourSlots(IEnumerable<OctopusPriceSlot> slots)
    {
        List<OctopusPriceSlot> result = new();

        foreach (var slot in slots)
        {
            var slotLength = slot.valid_to - slot.valid_from;

            if (slotLength.TotalMinutes == 30)
            {
                result.Add(slot);
                continue;
            }

            var start = slot.valid_from;
            while (start < slot.valid_to)
            {
                // We don't care about 30-minute slots in the past
                if (slot.valid_to < DateTime.UtcNow)
                    continue;
                
                var smallSlot = new OctopusPriceSlot
                {
                    valid_from = start,
                    valid_to = start.AddMinutes(30),
                    ActionReason = slot.ActionReason,
                    ManualOverrideAction = slot.ManualOverrideAction,
                    PlanAction = slot.PlanAction,
                    PriceType = slot.PriceType,
                    value_inc_vat = slot.value_inc_vat,
                    pv_est_kwh = slot.pv_est_kwh
                };
                
                if( smallSlot.valid_to > DateTime.UtcNow)
                    result.Add(smallSlot);
                
                start = start.AddMinutes(30);
            }
        }

        return result;
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

        try
        {
            var response = await "https://api.octopus.energy/"
                .WithHeader("Authorization", token)
                .AppendPathSegment($"/v1/accounts/{accountNumber}/")
                .GetStringAsync();

            var result = JsonSerializer.Deserialize<OctopusAccountDetails>(response);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get Octopus account details");
        }

        return null;
    }

    /// <summary>
    /// To identify the product code for a particular tariff, you can usually take off the first few letters of
    /// the tariff (E-1R-, E-2R- or G-1R) which indicate if it is electricity single register, electricity dual
    /// register (eg economy7) or gas single register, and the letter at the end (eg -A) which indicates the
    /// region code. So, for example, E-1R-VAR-19-04-12-N is one of the tariffs for product VAR-19-04-12.
    /// </summary>
    /// <param name="tariffCode"></param>
    /// <returns></returns>
    public static string GetProductFromTariffCode(string tariffCode)
    {
        if (string.IsNullOrEmpty(tariffCode))
            return string.Empty;
        
        var lastDash = tariffCode.LastIndexOf('-');
        if( lastDash > 0 )
            tariffCode = tariffCode.Substring(0, lastDash);

        // Hacky, but we don't do it very often, so meh
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
            var now = DateTime.UtcNow;
            var currentProperty = accountDetails.properties.FirstOrDefault(x => x.moved_in_at < now && x.moved_out_at == null);

            if (currentProperty != null)
            {
                var exportMeter = currentProperty.electricity_meter_points.FirstOrDefault(x => !x.is_export);
                if (exportMeter != null)
                {
                    // Look for a contract with no end date.
                    var contract = exportMeter.agreements.FirstOrDefault(x => x.valid_from < now && x.valid_to == null);

                    // It's possible it has an end-date set, that's later than today. 
                    if( contract == null )
                        contract = exportMeter.agreements.FirstOrDefault(x => x.valid_from < now && x.valid_to != null && x.valid_to > now);

                    if (contract != null)
                    {
                        logger.LogInformation("Found Octopus Product/Contract: {P}, Starts {S}",
                            contract.tariff_code, contract.valid_from);
                        return contract.tariff_code;
                    }
                }
            }
        }

        return null;
    }

    public record OctopusAgreement(string tariff_code, DateTime? valid_from, DateTime? valid_to);
    public record OctopusMeter(string serial_number);
    public record OctopusMeterPoints(string mpan, OctopusMeter[] meters, OctopusAgreement[] agreements, bool is_export);
    public record OctopusProperty(int id, OctopusMeterPoints[] electricity_meter_points, DateTime? moved_in_at, DateTime? moved_out_at);
    public record OctopusAccountDetails(string number, OctopusProperty[] properties);
    
    private record OctopusPrices(int count, OctopusPriceSlot[] results);
}