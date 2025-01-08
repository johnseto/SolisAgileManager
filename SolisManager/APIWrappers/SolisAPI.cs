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

    public SolisAPI(SolisManagerConfig _config, ILogger<SolisAPI> _logger)
    {
        config = _config;
        logger = _logger;
        client.BaseAddress = new Uri("https://www.soliscloud.com:13333");
    }

    public async Task<InverterDetails> InverterState()
    {
        var result = await Post<InverterDetails>(1,"inverterDetail", 
            new { sn = config.SolisInverterSerial
            });
        return result;
    }

    public async Task<IReadOnlyList<UserStation>> UserStationList(int pageNo, int pageSize)
    {
        var result = await Post<ListResponse<UserStation>>(1,"userStationList", new UserStationListRequest(pageNo, pageSize));
        return result.data.page.records;
    }

    public async Task<IReadOnlyList<Inverter>> InverterList(int pageNo, int pageSize, int? stationId)
    {
        var result = await Post<ListResponse<Inverter>>(1,"inverterList", new InverterListRequest(pageNo, pageSize, stationId));
        return result.data.page.records;
    }

    public async Task<Dictionary<string, object>?> AtRead()
    {
        var result = await Post<Dictionary<string, object>>(2,"atRead", new { inverterSn = config.SolisInverterSerial, cid = 4643 } );
        return result;
    }

    /// <summary>
    /// Set the inverter to charge or discharge for a particular period
    /// </summary>
    /// <param name="inverterSn"></param>
    /// <param name="slotStart">E.g., 08:30</param>
    /// <param name="slotEnd">E.g., 09:00</param>
    /// <param name="charge">True to charge, false to discharge</param>
    /// <param name="simulateOnly">Goes through the motions but doesn't actually charge</param>
    /// <returns></returns>
    public async Task<object?> SetCharge( DateTime slotStart, DateTime slotEnd, bool charge, bool simulateOnly )
    {
            int chargePower = charge ? config.MaxChargeRateAmps : 0;
            int dischargePower = charge ? 0 : config.MaxChargeRateAmps;
            var chargeValues = $"{chargePower},{dischargePower},{slotStart:HH:mm}-{slotEnd:HH:mm},00:00-00:00,0,0,00:00-00:00,00:00-00:00,0,0,00:00-00:00,00:00-00:00";
            var requestBody = new
            {
                inverterSn = config.SolisInverterSerial,
                cid = 103,
                value = chargeValues
            };

            if (simulateOnly)
            {
                logger.LogInformation("Simulate Charge request: {P}", JsonSerializer.Serialize(requestBody) );
                return null;
            }
            else
            {
                // Actually submit it. 
                return await Post<object>(2, "control", requestBody);
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

            logger.LogInformation("Posted request to SolisCloud: {U} {C}", url, content);

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

public record InverterDetails(InverterData data);
public record InverterData(IEnumerable<Battery> batteryList);
public record Battery(int batteryCapacitySoc);
public record UserStation(string id, string installer, string installerId, double allEnergy1, double allIncome,
    double dayEnergy1, double dayIncome, double gridPurchasedTodayEnergy, double gridPurchasedTotalEnergy,
    double gridSellTodayEnergy, double gridSellTotalEnergy, double homeLoadTodayEnergy, double homeLoadTotalEnergy,
    double monthEnergy1, double power1, double yearEnergy1);

public record Inverter(string id, string collectorId, string collectorSn, string dataTimestamp, string dataTimestampStr,
    double etoday1, double etotal1, double familyLoadPower, double gridPurchasedTodayEnergy, double gridSellTodayEnergy,
    double homeLoadTodayEnergy, double pac1, double pow1, double pow2, double power1, double totalFullHour,
    double totalLoadPower);