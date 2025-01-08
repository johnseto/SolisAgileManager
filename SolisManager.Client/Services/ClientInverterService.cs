using System.Net.Http.Json;
using System.Text.Json;
using SolisManager.Shared;
using SolisManager.Shared.Models;

namespace SolisManager.Client.Services;

public class ClientInverterService( HttpClient httpClient ) : IInverterService
{
    public async Task<SolisManagerState?> GetAgilePriceSlots()
    {
        return await httpClient.GetFromJsonAsync<SolisManagerState>("inverter/agileprices");
    }

    public async Task<SolisManagerConfig> GetConfig()
    {
        return await httpClient.GetFromJsonAsync<SolisManagerConfig>("inverter/getconfig");
    }

    public async Task SaveConfig(SolisManagerConfig config)
    {
        // TODO - investigate why passing the object directly, rather than the json
        // as a queryparam, doesn't work. 
        var json = JsonSerializer.Serialize(config);
        await httpClient.PostAsync($"inverter/saveconfig?configJson={json}", null);
    }

    public async Task ClearOverrides()
    {
        await httpClient.GetAsync("inverter/clearoverrides");
    }
    
    public async Task ChargeBattery()
    {
        await httpClient.GetAsync("inverter/chargebattery");
    }

    public async Task DischargeBattery()
    {
        await httpClient.GetAsync("inverter/dischargebattery");
    }

    public async Task DumpAndChargeBattery()
    {
        await httpClient.GetAsync("inverter/dumpandchargebattery");
    }
}