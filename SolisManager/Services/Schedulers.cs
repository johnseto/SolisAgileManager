using Coravel.Invocable;
using SolisManager.APIWrappers;
using SolisManager.Shared;

namespace SolisManager.Services;

public class RatesScheduler( IInverterRefreshService service, ILogger<RatesScheduler> logger ) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogDebug("Executing Rates scheduler");
        await service.RefreshAgileRates();
    }
}

public class BatteryScheduler( IInverterRefreshService service, ILogger<BatteryScheduler> logger) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogDebug("Executing Battery scheduler");
        await service.RefreshBatteryState();
    }
}

public class SolcastScheduler( SolcastAPI solcastService, ILogger<SolcastScheduler> logger ) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogDebug("Executing Solcast scheduler");
        await solcastService.UpdateSolcastDataFromAPI();
    }
}


public class TariffScheduler( IInverterRefreshService inverterRefresh, ILogger<SolcastScheduler> logger ) : IInvocable
{
    public async Task Invoke()
    {
        await inverterRefresh.RefreshTariff();
    }
}

public class VersionCheckScheduler( InverterManager service, ILogger<BatteryScheduler> logger) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogDebug("Executing version check scheduler");
        await service.CheckForNewVersion();
    }
}
