using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SolisManager.Shared.Models;

namespace SolisManager.APIWrappers;

/// A wrapper for the Solis API Based on Jon Glass's implementation
/// here: https://github.com/jmg48/solis-cloud
/// But extended to support setting charges (based on
/// https://github.com/stevegal/solis-control
public class SolisAPI
{
    private readonly HttpClient client = new();
    private readonly ILogger<SolisAPI> logger;
    private readonly SolisManagerConfig config;

    private string simulatedChargeState = string.Empty;
    
    public SolisAPI(SolisManagerConfig _config, ILogger<SolisAPI> _logger)
    {
        config = _config;
        logger = _logger;
        client.BaseAddress = new Uri("https://www.soliscloud.com:13333");
    }

    public async Task<InverterDetails?> InverterState()
    {
        var result = await Post<InverterDetails>(1,"inverterDetail", 
            new { sn = config.SolisInverterSerial
            });
        return result;
    }

    public async Task<IReadOnlyList<UserStation>> UserStationList(int pageNo, int pageSize)
    {
        var result = await Post<ListResponse<UserStation>>(1,"userStationList", new UserStationListRequest(pageNo, pageSize));
        if(result != null)
            return result.data.page.records;

        return [];
    }

    public async Task<IReadOnlyList<Inverter>> InverterList(int pageNo, int pageSize, int? stationId)
    {
        var result = await Post<ListResponse<Inverter>>(1,"inverterList", new InverterListRequest(pageNo, pageSize, stationId));
        if( result != null )
            return result.data.page.records;
        return [];
    }

    private async Task<ChargeStateData?> ReadChargingState()
    {
        if (config.Simulate && !string.IsNullOrEmpty(simulatedChargeState))
        { 
            return ChargeStateData.FromChargeStateData(simulatedChargeState);
        }

        var result = await Post<AtReadResponse>(2, "atRead", new { inverterSn = config.SolisInverterSerial, cid = 4643 });

        if (result != null && !string.IsNullOrEmpty(result.data.msg))
        {
            simulatedChargeState = result.data.msg;
            return ChargeStateData.FromChargeStateData(result.data.msg);
        }

        return null;
    }

    /// <summary>
    /// Convert a time-slot string, e.g., "05:30-10:45" into an actual date time
    /// so we can compare it.
    /// </summary>
    /// <param name="chargeTimePair"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    private (DateTime start, DateTime end) ConvertToRealDates(string chargeTimePair)
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var nowTime = TimeOnly.FromDateTime(DateTime.UtcNow);
        
        var parts = chargeTimePair.Split('-', 2, StringSplitOptions.TrimEntries);

        if (parts.Length != 2)
            throw new ArgumentException($"Invalid time pair {chargeTimePair}");

        if (!TimeOnly.TryParse(parts[0], out var startTime))
            throw new ArgumentException($"Invalid time pair {chargeTimePair}");

        if (!TimeOnly.TryParse(parts[1], out var endTime))
            throw new ArgumentException($"Invalid time pair {chargeTimePair}");

        var start = new DateTime(now, startTime);
        var end = new DateTime(now, endTime);

        if (startTime < nowTime) 
        {
            start = start.AddDays(1);    
            end = end.AddDays(1);    
        }

        if (endTime < startTime)
            end = end.AddDays(1);
        
        return (start, end);
    }
    
    /// <summary>
    /// Complicated logic to determine if the new charge and discharge settings are materially different
    /// to what's in the inverter at the moment. So we check the current/amps for charge and discharge,
    /// and also check whether or not the charging time for the new settings starts within the existing
    /// settings, and has the same end-time. If so, then there's no point submitting a change to the
    /// inverter, as it won't make any difference to the behaviour.
    /// </summary>
    /// <param name="chargePower"></param>
    /// <param name="dischargePower"></param>
    /// <param name="chargeTimes"></param>
    /// <param name="dischargeTimes"></param>
    /// <returns></returns>
    private async Task<bool> InverterNeedsUpdating(int chargePower, int dischargePower, string chargeTimes, string dischargeTimes)
    {
        // Get the current state of the inverter
        var currentChargeState = await ReadChargingState();

        // If for some reason we didn't get the current state, we'll *have* to write
        if (currentChargeState == null)
            return true;
        
        var newchargeTime = ConvertToRealDates(chargeTimes);
        var newdischargeTime = ConvertToRealDates(dischargeTimes);
        var currchargeTime = ConvertToRealDates(currentChargeState.chargeTimes);
        var currdischargeTime = ConvertToRealDates(currentChargeState.dischargeTimes);
        
        bool chargeIsEquivalent = newchargeTime.start >= currchargeTime.start &&
                                  newchargeTime.start <= currchargeTime.end &&
                                  newchargeTime.end == currchargeTime.end &&
                                  chargePower == currentChargeState.chargeAmps;

        bool dischargeIsEquivalent = newdischargeTime.start >= currdischargeTime.start &&
                                  newdischargeTime.start <= currdischargeTime.end &&
                                  newdischargeTime.end == currdischargeTime.end &&
                                  dischargePower == currentChargeState.dischargeAmps;

        if (!chargeIsEquivalent)
            return true;

        if (!dischargeIsEquivalent)
            return true;
        
        return false;
    }
    
    /// <summary>
    /// Set the inverter to charge or discharge for a particular period
    /// </summary>
    /// <returns></returns>
    public async Task SetCharge( DateTime? chargeStart, DateTime? chargeEnd, 
                                          DateTime? dischargeStart, DateTime? dischargeEnd, 
                                          bool simulateOnly )
    {
        const string clearChargeSlot = "00:00-00:00";

        var chargeTimes = clearChargeSlot;
        var dischargeTimes = clearChargeSlot;
        int chargePower = 0;
        int dischargePower = 0;

        if (chargeStart != null && chargeEnd != null)
        {
            chargeTimes = $"{chargeStart:HH:mm}-{chargeEnd:HH:mm}";
            chargePower = config.MaxChargeRateAmps;
        }

        if (dischargeStart != null && dischargeEnd != null)
        {
            dischargeTimes = $"{dischargeStart:HH:mm}-{dischargeEnd:HH:mm}";
            dischargePower = config.MaxChargeRateAmps;
        }

        // Now check if we actually need to do anything. No point making a write call to the 
        // inverter if it's already in the correct state. It's an EEPROM, so the fewer writes
        // we can do for longevity, the better.
        if (await InverterNeedsUpdating(chargePower, dischargePower, chargeTimes, dischargeTimes))
        {
            string chargeValues = $"{chargePower},{dischargePower},{chargeTimes},{dischargeTimes},0,0,00:00-00:00,00:00-00:00,0,0,00:00-00:00,00:00-00:00";

            var requestBody = new
            {
                inverterSn = config.SolisInverterSerial,
                cid = 103,
                value = chargeValues
            };

            logger.LogInformation("Sending new charge instruction to {Inv}: {CA}, {DA}, {CT}, {DT}", 
                            simulateOnly ? "mock inverter" : "Solis Inverter",
                            chargePower, dischargePower, chargeTimes, dischargeTimes);

            if (simulateOnly)
            {
                simulatedChargeState = chargeValues;
            }
            else
            {
                // Actually submit it. 
                var result = await Post<object>(2, "control", requestBody);
            }
        }
        else
        {
            logger.LogInformation("Skipping charge request (Inverter state matches: {CA}, {DA}, {CT}, {DT})", 
                                                chargePower, dischargePower, chargeTimes, dischargeTimes);
        }
    }

    private async Task<T?> Post<T>(int apiVersion, string resource, object body)
    {
        var content = JsonSerializer.Serialize(body);
        var response = await Post($"/v{apiVersion}/api/{resource}", content);
        return JsonSerializer.Deserialize<T>(response);
    }

    private async Task<string> Post(string url, string content)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            var date = DateTime.UtcNow.ToString("ddd, d MMM yyyy HH:mm:ss 'GMT'");

            request.Content = new StringContent(content);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json;charset=UTF-8");

            var contentMd5 = Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(content)));
            var hmacSha1 = new HMACSHA1(Encoding.UTF8.GetBytes(config.SolisAPISecret));
            var param = $"POST\n{contentMd5}\napplication/json\n{date}\n{url}";
            var sign = Convert.ToBase64String(hmacSha1.ComputeHash(Encoding.UTF8.GetBytes(param)));
            var auth = $"API {config.SolisAPIKey}:{sign}";

            request.Headers.Add("Time", date);
            request.Headers.Add("Authorization", auth);
            request.Content.Headers.Add("Content-Md5", contentMd5);

            var result = await client.SendAsync(request);

            result.EnsureSuccessStatusCode();

            logger.LogDebug("Posted request to SolisCloud: {U} {C}", url, content);

            return await result.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error posting request to SolisCloud: {U} {C}", url, content);
        }

        return string.Empty;
    }
}

public record UserStationListRequest(int pageNo, int pageSize);

public record InverterListRequest(int pageNo, int pageSize, int? stationId);

public record ListResponse<T>(Data<T> data);

public record Data<T>(Page<T> page);

public record Page<T>(int current, int pages, List<T> records);

public record AtReadData(string msg);
public record AtReadResponse(AtReadData data);

public record ChargeStateData( int chargeAmps, int dischargeAmps, string chargeTimes, string dischargeTimes)
{
    public static ChargeStateData FromChargeStateData(string msg)
    {
        var parts = msg.Split(',', 5, StringSplitOptions.TrimEntries);
        if (parts.Length == 5)
        {
            return new ChargeStateData(
                int.Parse(parts[0]),
                int.Parse(parts[1]),
                parts[2],
                parts[3]);
        }

        throw new AggregateException("Unable to parse atRead response");
    }
}

public record InverterDetails(InverterData data);
public record InverterData(IEnumerable<Battery> batteryList, decimal eToday, decimal pac, string stationId, decimal batteryPower, decimal psum);
public record Battery(int batteryCapacitySoc);
public record UserStation(string id, string installer, string installerId, double allEnergy1, double allIncome,
    double dayEnergy1, double dayIncome, double gridPurchasedTodayEnergy, double gridPurchasedTotalEnergy,
    double gridSellTodayEnergy, double gridSellTotalEnergy, double homeLoadTodayEnergy, double homeLoadTotalEnergy,
    double monthEnergy1, double power1, double yearEnergy1);

public record Inverter(string id, string collectorId, string collectorSn, string dataTimestamp, string dataTimestampStr,
    double etoday1, double etotal1, double familyLoadPower, double gridPurchasedTodayEnergy, double gridSellTodayEnergy,
    double homeLoadTodayEnergy, double pac1, double pow1, double pow2, double power1, double totalFullHour,
    double totalLoadPower);