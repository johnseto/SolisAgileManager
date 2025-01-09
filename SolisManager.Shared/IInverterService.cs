using SolisManager.Shared.Models;

namespace SolisManager.Shared;

public interface IInverterService
{
    public Task<SolisManagerState?> GetAgilePriceSlots();
    public Task<List<HistoryEntry>> GetHistory();
    public Task<SolisManagerConfig> GetConfig();
    public Task SaveConfig(SolisManagerConfig config);

    public Task TestCharge();
    public Task ChargeBattery();
    public Task DischargeBattery();
    public Task DumpAndChargeBattery();
    public Task ClearOverrides();
}

public interface IInverterRefreshService
{
    public Task RefreshBatteryState();
    public Task RefreshSolcastData();
    public Task RefreshAgileRates();
}