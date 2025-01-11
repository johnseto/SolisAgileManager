using System.Net.Http.Json;
using System.Text.Json;
using SolisManager.Shared;
using SolisManager.Shared.Models;

namespace SolisManager.Client.Services;

public class ClientInverterService( HttpClient httpClient ) : IInverterService
{
    public SolisManagerState InverterState { get; private set; } = new();

    public async Task RefreshInverterState()
    {
        // Load the data from the server
        var state = await httpClient.GetFromJsonAsync<SolisManagerState?>("inverter/refreshinverterdata");
        if (state != null)
            InverterState = state;
    }

    public async Task CancelSlotAction(OctopusPriceSlot slot)
    {
        await httpClient.PostAsJsonAsync("inverter/cancelslotaction", slot);
    }
    public async Task<List<HistoryEntry>> GetHistory()
    {
        var result = await httpClient.GetFromJsonAsync<List<HistoryEntry>>("inverter/history");
        if (result != null)
            return result;

        return [];
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

    public async Task AdvanceSimulation()
    {
        await httpClient.GetAsync("inverter/advancesimulation");
    }

    public async Task ResetSimulation()
    {
        await httpClient.GetAsync("inverter/resetsimulation");
    }

    public async Task TestCharge()
    {
        await httpClient.GetAsync("inverter/testcharge");
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