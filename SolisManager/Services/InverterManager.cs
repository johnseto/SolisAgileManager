using System.Diagnostics;
using Octokit;
using SolisManager.APIWrappers;
using SolisManager.Extensions;
using SolisManager.Shared;
using SolisManager.Shared.Models;

namespace SolisManager.Services;

public class InverterManager(
    SolisManagerConfig config,
    OctopusAPI octopusAPI,
    SolisAPI solisApi,
    SolcastAPI solcastApi,
    ILogger<InverterManager> logger) : IInverterService, IInverterRefreshService
{
    public SolisManagerState InverterState { get; } = new();

    private readonly Dictionary<DateTime, OctopusPriceSlot> manualOverrides = new();
    private readonly List<HistoryEntry> executionHistory = new();
    private const string executionHistoryFile = "SolisManagerExecutionHistory.csv";
    private NewVersionResponse appVersion = new();
    private List<OctopusPriceSlot>? simulationData;
    
    
    private async Task EnrichWithSolcastData(IEnumerable<OctopusPriceSlot>? slots)
    {
        var solcast = await solcastApi.GetSolcastForecast();
        
        if (solcast.forecasts == null || !solcast.forecasts.Any())
            return;
        
        InverterState.ForecastDayLabel = "today";
        var forecast = solcast.forecasts?.Where(x => x.PeriodStart.Date == DateTime.Today)
            .Sum(x => x.ForecastkWh!);
        if (forecast == null || forecast.Value == 0)
        { 
            InverterState.ForecastDayLabel = "tomorrow";
            forecast = solcast.forecasts?.Where(x => x.PeriodStart.Date == DateTime.Today.AddDays(1))
                .Sum(x => x.ForecastkWh!);
        }

        InverterState.ForecastPVkWh = forecast;
        InverterState.SolcastTimeStamp = solcast.lastApiUpdate;

        if (slots != null && slots.Any())
        {
            var lookup = solcast.forecasts.ToDictionary(x => x.PeriodStart);

            var matchedData = false;
            foreach (var slot in slots)
            {
                if (lookup.TryGetValue(slot.valid_from, out var solcastEstimate))
                {
                    slot.pv_est_kwh = solcastEstimate.ForecastkWh;
                    matchedData = true;
                }
                else
                {
                    // No data
                    slot.pv_est_kwh = null;
                }
            }
            
            if( ! matchedData )
                logger.LogError("Solcast Data was retrieved, but no entries matched current slots");
        }
    }

    private async Task AddToExecutionHistory(OctopusPriceSlot slot)
    {
        try
        {
            var historyFilePath = Path.Combine(Program.ConfigFolder, executionHistoryFile);

            var newEntry = new HistoryEntry(slot, InverterState.BatterySOC);
            var lastEntry = executionHistory.LastOrDefault();

            if (lastEntry == null || lastEntry.Start != newEntry.Start)
            {
                // Add the item
                executionHistory.Add(newEntry);

                // And write
                await File.WriteAllLinesAsync(historyFilePath, executionHistory.Select(x => x.GetAsCSV()));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add entry to execution history");
        }
    }

    private async Task LoadExecutionHistory()
    {
        try
        {
            var historyFilePath = Path.Combine(Program.ConfigFolder, executionHistoryFile);

            if (!executionHistory.Any() && File.Exists(historyFilePath))
            {
                var lines = await File.ReadAllLinesAsync(historyFilePath);
                logger.LogInformation("Loaded {C} entries from execution history file {F}", lines.Length,
                    executionHistoryFile);

                // At 48 slots per day, we store 180 days or 6 months of data
                var entries = lines.TakeLast(180 * 48)
                    .Select(x => HistoryEntry.TryParse(x))
                    .Where(x => x != null)
                    .Select(x => x!)
                    .ToList();

                executionHistory.AddRange(entries);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load execution history");
        }
    }

    private async Task RefreshData()
    {
        // Don't even attempt this if there's no config
        if (!config.IsValid())
            return;

        IEnumerable<OctopusPriceSlot> slots;

        if (config.Simulate && simulationData != null)
        {
            slots = simulationData;
        }
        else
        {
            var lastSlot = InverterState.Prices?.MaxBy(x => x.valid_from);
            
            logger.LogTrace("Refreshing data...");

            var octRatesTask = octopusAPI.GetOctopusRates();

            await Task.WhenAll(RefreshBatteryState(), octRatesTask, LoadExecutionHistory());

            // Stamp the last time we did an update
            InverterState.TimeStamp = DateTime.UtcNow;

            // Now, process the octopus rates
            slots = (await octRatesTask).ToList();

            if(slots.Any())
            {
                var newlatestSlot = slots.MaxBy(x => x.valid_from);

                if (newlatestSlot != null && (lastSlot == null || newlatestSlot.valid_from > lastSlot.valid_from))
                {
                    var newslots = (lastSlot == null ? slots : 
                            slots.Where(x => x.valid_from > lastSlot.valid_from)).ToList();

                    var newSlotCount = newslots.Count;
                    var cheapest = newslots.Min(x => x.value_inc_vat);
                    var peak = newslots.Max(x => x.value_inc_vat);

                    logger.LogInformation("{N} new Octopus rates available to {L:dd-MMM-yyyy HH:mm} (cheapest: {C}p/kWh, peak: {P}p/kWh)",
                        newSlotCount, newlatestSlot.valid_to, cheapest, peak);
                }
            }

            if (config.Simulate)
            {
                simulationData = slots.ToList();

                if( Debugger.IsAttached)
                {
                    //CreateSomeNegativeSlots(slots);
                    //CreateSomeNegativeSlots(slots);
                }

            }
        }

        await EnrichWithSolcastData(slots);
        
        var processedSlots = EvaluateSlotActions(slots.ToArray());

        // Update the state
        InverterState.Prices = processedSlots;

        await ExecuteSlotChanges(processedSlots);

        CleanupOldOverrides();
    }

    private async Task ExecuteSlotChanges(IEnumerable<OctopusPriceSlot> slots)
    {
        var firstSlot = slots.FirstOrDefault();
        if (firstSlot != null)
        {
            if (!config.Simulate)
                await AddToExecutionHistory(firstSlot);

            var matchedSlots = slots.TakeWhile(x => x.Action == firstSlot.Action).ToList();

            if (matchedSlots.Any())
            {
                logger.LogDebug("Found {N} slots with matching action to conflate", matchedSlots.Count);

                // The timespan is from the start of the first slot, to the end of the last slot.
                var start = matchedSlots.First().valid_from;
                var end = matchedSlots.Last().valid_to;

                if (firstSlot.Action == SlotAction.Charge)
                {
                    await solisApi.SetCharge(start, end, null, null, config.Simulate);
                }
                else if (firstSlot.Action == SlotAction.Discharge)
                {
                    await solisApi.SetCharge(null, null, start, end, config.Simulate);
                }
                else
                {
                    // Clear the charge
                    await solisApi.SetCharge(null, null, null, null, config.Simulate);
                }
            }
        }
    }
    
    private List<OctopusPriceSlot> EvaluateSlotActions(OctopusPriceSlot[]? slots)
    {
        if (slots == null)
            return [];

        logger.LogTrace("Evaluating slot actions...");

        try
        {
            OctopusPriceSlot[]? cheapestSlots = null;
            OctopusPriceSlot[]? priciestSlots = null;

            // Calculate how many slots we'd need to charge from full starting *right now*
            int chargeSlotsNeeededNow = (int)Math.Round(config.SlotsForFullBatteryCharge * config.PeakPeriodBatteryUse, MidpointRounding.ToPositiveInfinity);

            // First, find the cheapest period for charging the battery. This is the set of contiguous
            // slots, long enough when combined that they can charge the battery from empty to full, and
            // that has the cheapest average price for that period. This will typically be around 1am in 
            // the morning, but can shift around a bit. 
            for (var i = 0; i <= slots.Length - config.SlotsForFullBatteryCharge; i++)
            {
                var chargePeriod = slots[i .. (i + config.SlotsForFullBatteryCharge)];
                var chargePeriodTotal = chargePeriod.Sum(x => x.value_inc_vat);

                if (cheapestSlots == null || chargePeriodTotal < cheapestSlots.Sum(x => x.value_inc_vat))
                    cheapestSlots = chargePeriod;
            }

            if (cheapestSlots != null && cheapestSlots.First().valid_from == slots[0].valid_from)
            {
                // If the cheapest period starts *right now* then reduce the number of slots
                // required down based on the battery SOC. E.g., if we've got 6 slots, but
                // the battery is 50% full, we don't need all six. So take the n cheapest. 
                cheapestSlots = cheapestSlots.OrderBy(x => x.value_inc_vat)
                                             .Take(chargeSlotsNeeededNow)
                                             .ToArray();
            }

            // Similar calculation for the peak period.
            int peakPeriodLength = 7; // Peak period is usually 4pm - 7:30pm, so 7 slots.
            for (var i = 0; i <= slots.Length - peakPeriodLength; i++)
            {
                var peakPeriod = slots[i .. (i + peakPeriodLength)];
                var peakPeriodTotal = peakPeriod.Sum(x => x.value_inc_vat);

                if (priciestSlots == null || peakPeriodTotal > priciestSlots.Sum(x => x.value_inc_vat))
                    priciestSlots = peakPeriod;
            }

            // First, mark the priciest slots as 'peak'. That way we'll avoid them at all cost.
            if (priciestSlots != null)
            {
                foreach (var slot in priciestSlots)
                {
                    slot.PriceType = PriceType.MostExpensive;
                    slot.ActionReason = "Peak price slot - avoid charging";
                }
            }

            if (cheapestSlots != null)
            {
                // Now mark the cheapest slots - unless they happen to coincide with the most expensive
                // which can happen in scenarios where there's only 5-10 slots before the new tariff 
                // data comes in. This is really a display issue, as by the time we get to these slots
                // we'll have more data and a better cheaper slot will have been found
                foreach (var slot in cheapestSlots.Where(x => x.PriceType != PriceType.MostExpensive))
                {
                    slot.PriceType = PriceType.Cheapest;
                    slot.Action = SlotAction.Charge;
                    slot.ActionReason = "This is the cheapest set of slots, to fully charge the battery";
                }
            }

            // Now, we've calculated the cheapest and most expensive slots. From the remaining slots, calculate
            // the average rate across them. We then use that average rate to determine if any other slots across
            // the day are a bit cheaper. So look for anything that's 90% of the average, or below, and mark it
            // as BelowAverage. For those slots, if the battery is low, we'll take the opportunity to charge as 
            // they're a bit cheaper-than-average.
            var averagePriceSlots = slots.Where(x => x.PriceType == PriceType.Average).ToList();

            if (averagePriceSlots.Any())
            {
                var averagePrice = decimal.Round(averagePriceSlots.Average(x => x.value_inc_vat), 2);
                decimal cheapThreshold = averagePrice * (decimal)0.9;

                foreach (var slot in slots.Where(x =>
                             x.PriceType == PriceType.Average && x.value_inc_vat < cheapThreshold))
                {
                    slot.PriceType = PriceType.BelowAverage;
                    slot.Action = SlotAction.ChargeIfLowBattery;
                    slot.ActionReason =
                        $"Price is at least 10% below the average price of {averagePrice}p/kWh, so flagging as potential top-up";
                }
            }

            if (cheapestSlots != null)
            {
                // If we have a set of cheapest slots, then the price will usually start to 
                // drop a few slots before it's actually cheapest; these will likely be 
                // slots that are BelowAverage pricing in the run-up to the cheapest period.
                // However, we don't want to charge then, because otherwise by the time we
                // get to the cheapest period, the battery will be full. So back up n slots
                // and even if they're BelowAverage, remove their charging instruction.
                var firstCheapest = cheapestSlots.First();

                bool beforeCheapest = false;
                int dipSlots = config.SlotsForFullBatteryCharge;
                
                foreach (var slot in slots.Reverse())
                {
                    if (slot.Id == firstCheapest.Id)
                    {
                        beforeCheapest = true;
                        continue;
                    }

                    if (beforeCheapest && slot.PriceType == PriceType.BelowAverage)
                    {
                        slot.PriceType = PriceType.Dropping;
                        slot.Action = SlotAction.DoNothing;
                        slot.ActionReason = "Price is falling in the run-up to the cheapest period, so don't charge";
                        dipSlots--;
                        if (dipSlots == 0)
                            break;
                    }
                }
            }

            if (priciestSlots != null)
            {
                // If we have a set of priciest slots, we want to charge before them. Now, it doesn't 
                // matter if the charging slots aren't all contiguous - so we can have a bit of 
                // flexibility. We also only need to charge the battery enough to get us to the 
                // PeakPeriodBatteryUse percentage (e.g., 50%). 
                var chargeSlotChoices = slots.GetPreviousNItems(chargeSlotsNeeededNow + 2, x => x.valid_from == priciestSlots.First().valid_from);

                if (chargeSlotChoices.Any())
                {
                    // Get the pre-peak slot choices, sorted by price
                    var prePeakSlots = chargeSlotChoices.OrderBy(x => x.value_inc_vat)
                        .Take(chargeSlotsNeeededNow)
                        .ToList();

                    foreach (var prePeakSlot in prePeakSlots)
                    {
                        // It's expensive, but not terrible. Suck it up and charge
                        prePeakSlot.Action = SlotAction.Charge;
                        prePeakSlot.ActionReason = $"Cheaper slot to ensure battery is charged to {config.PeakPeriodBatteryUse:P0} before the peak period";
                    }
                }
            }

            // If there are any slots below our "Blimey it's cheap" threshold, elect to charge them anyway.
            foreach (var slot in slots.Where(s => s.value_inc_vat < config.AlwaysChargeBelowPrice))
            {
                slot.PriceType = PriceType.BelowThreshold;
                slot.Action = SlotAction.Charge;
                slot.ActionReason =
                    $"Price is below the threshold of {config.AlwaysChargeBelowPrice}p/kWh, so always charge";
            }

            foreach (var slot in slots.Where(s => s.value_inc_vat < 0))
            {
                slot.PriceType = PriceType.Negative;
                slot.Action = SlotAction.Charge;
                slot.ActionReason = "Negative price - always charge";
            }

            // For any slots that are set to "charge if low battery", update them to 'charge' if the 
            // battery SOC is, indeed, low. Only do this for enough slots to fully charge the battery.
            if (InverterState.BatterySOC < config.LowBatteryPercentage)
            {
                foreach (var slot in slots.Where(x => x.Action == SlotAction.ChargeIfLowBattery)
                             .Take(config.SlotsForFullBatteryCharge))
                {
                    slot.Action = SlotAction.Charge;
                    slot.ActionReason =
                        $"Upcoming slot is set to charge if low battery; battery is currently at {InverterState.BatterySOC}%";
                }
            }
            
            // Now it gets interesting. Find the groups of slots that have negative prices. So we
            // might end up with 3 negative prices, and another group of 7 negative prices. For any
            // groups that are long enough to charge the battery fully, discharge the battery for 
            // all the slots that aren't needed to recharge the battery. 
            // NOTE/TODO: We should check, and if any of the groups of negative slots are *now*
            // then we should factor in the SOC.
            var negativeSpans = slots.GetAdjacentGroups(x => x.PriceType == PriceType.Negative);

            foreach (var negSpan in negativeSpans)
            {
                if (negSpan.Count() > config.SlotsForFullBatteryCharge)
                {
                    var dischargeSlots = negSpan.SkipLast(config.SlotsForFullBatteryCharge).ToList();

                    dischargeSlots.ForEach(x =>
                    {
                        x.Action = SlotAction.Discharge;
                        x.ActionReason =
                            "Contiguous negative slots allow the battery to be discharged and charged again.";
                    });
                }
            }
            
            foreach (var slot in slots)
            {
                if (manualOverrides.TryGetValue(slot.valid_from, out var manualOverride))
                {
                    slot.Action = manualOverride.Action;
                    slot.ActionReason = "Manual override applied.";
                    slot.IsManualOverride = true;
                }
                else
                    slot.IsManualOverride = false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected exception during slot action evaluation:");
        }

        return slots.ToList();
    }

    
    private void CreateSomeNegativeSlots(IEnumerable<OctopusPriceSlot> slots)
    {
        if (Debugger.IsAttached && ! slots.Any(x => x.value_inc_vat < 0))
        {
            var averageSlots = slots
                .OrderBy(x => x.valid_from)
                .Where(x => x.PriceType == PriceType.Average)
                .ToArray();
            
            var rand = new Random();
            var index = rand.Next(averageSlots.Length);
            List<OctopusPriceSlot> negs = [averageSlots[index]];
            for (int n = 0; n < rand.Next(5, 9); n++)
            {
                if (--index > 0)
                    negs.Add(averageSlots[index]);
            }

            foreach (var slot in negs)
                slot.value_inc_vat = (rand.Next(10, 100) / 10M) * -1;
        }
        
    }
    
    public Task RefreshInverterState()
    {
        // Nothing to do on the server side, the refresh is triggered by the scheduler
        return Task.CompletedTask;
    }

    public async Task RefreshBatteryState()
    {
        if (!config.IsValid())
            return;

        // Get the battery charge state from the inverter
        var solisState = await solisApi.InverterState();

        if (solisState != null)
        {
            InverterState.BatterySOC = solisState.data.batteryList
                .Select(x => x.batteryCapacitySoc)
                .FirstOrDefault();
            InverterState.BatteryTimeStamp = DateTime.UtcNow;
            InverterState.CurrentPVkW = solisState.data.pac;
            InverterState.TodayPVkWh = solisState.data.eToday;
            InverterState.StationId = solisState.data.stationId;
            InverterState.HouseLoadkW = solisState.data.pac - solisState.data.psum - solisState.data.batteryPower;
            
            logger.LogInformation("Refreshed state: SOC = {S}%, Current PV = {PV}kW, House Load = {L}kW, Forecast ({DL}): {F}",
                InverterState.BatterySOC, InverterState.CurrentPVkW, InverterState.HouseLoadkW, 
                InverterState.ForecastDayLabel, InverterState.ForecastPVkWh != null ? $"{InverterState.ForecastPVkWh}kWh" : "n/a" );
        }
    }
    
    public async Task RefreshAgileRates()
    {
        await RefreshData();
    }

    public Task<List<HistoryEntry>> GetHistory()
    {
        return Task.FromResult(executionHistory);
    }

    public Task<SolisManagerConfig> GetConfig()
    {
        return Task.FromResult(config);
    }

    public async Task SaveConfig(SolisManagerConfig newConfig)
    {
        logger.LogInformation("Saving config to server...");

        newConfig.CopyPropertiesTo(config);
        await config.SaveToFile(Program.ConfigFolder);
        await RefreshData();
    }

    public async Task CancelSlotAction(OctopusPriceSlot slot)
    {
        var overrides = CreateOverrides(slot.valid_from, SlotAction.DoNothing, 1);
        logger.LogInformation("Clearing slot action for {S}-{E}...", slot.valid_from, slot.valid_to);
        await SetManualOverrides(overrides);
    }

    private int NearestHalfHour(int minute) => minute - (minute % 30);

    private List<OctopusPriceSlot> CreateOverrides(DateTime start, SlotAction action, int slotCount)
    {
        var overrides = Enumerable.Range(0, slotCount)
            .Select(x => new OctopusPriceSlot())
            .ToList();

        var currentSlot =
            new DateTime(start.Year, start.Month, start.Day, start.Hour, NearestHalfHour(start.Minute), 0);
        foreach (var slot in overrides)
        {
            slot.valid_from = currentSlot;
            currentSlot = currentSlot.AddMinutes(30);
            slot.valid_to = currentSlot;
            slot.Action = action;
        }

        return overrides;
    }

    public async Task TestCharge()
    {
        logger.LogInformation("Starting test charge for 5 minutes");
        var start = DateTime.UtcNow;
        var end = start.AddMinutes(5);
        await solisApi.SetCharge(start, end, null, null, false);
    }

    public async Task ChargeBattery()
    {
        // Work out the percentage charge, and then calculate how many slots it'll take to achieve that
        double percentageToCharge = (100 - InverterState.BatterySOC) / 100.0;
        var slotsRequired = (int)Math.Round(config.SlotsForFullBatteryCharge * percentageToCharge,
            MidpointRounding.ToPositiveInfinity);

        var overrides = CreateOverrides(DateTime.UtcNow, SlotAction.Charge, slotsRequired);
        await SetManualOverrides(overrides);
    }

    public async Task DischargeBattery()
    {
        var overrides = CreateOverrides(DateTime.UtcNow, SlotAction.Discharge, CalculateDischargeSlots());
        await SetManualOverrides(overrides);
    }

    private int CalculateDischargeSlots()
    {
        double slotsRequired = config.SlotsForFullBatteryCharge * (InverterState.BatterySOC / 100.0);
        return (int)Math.Round(slotsRequired, MidpointRounding.ToPositiveInfinity);
    }

    public async Task DumpAndChargeBattery()
    {
        var discharge = CreateOverrides(DateTime.UtcNow, SlotAction.Discharge, CalculateDischargeSlots());
        var charge = CreateOverrides(discharge.Last().valid_to, SlotAction.Charge, config.SlotsForFullBatteryCharge);
        await SetManualOverrides(discharge.Concat(charge).ToList());
    }

    private async Task SetManualOverrides(List<OctopusPriceSlot> overrides)
    {
        foreach (var overRide in overrides)
        {
            manualOverrides[overRide.valid_from] = overRide;
            logger.LogInformation("Added override: {S}", overRide);
        }

        await RefreshData();
    }

    private void CleanupOldOverrides()
    {
        var lookup = InverterState.Prices.ToDictionary(x => x.valid_from);

        foreach (var overRide in manualOverrides)
            if (!lookup.ContainsKey(overRide.Value.valid_from))
                manualOverrides.Remove(overRide.Value.valid_from);

    }

    public async Task ClearOverrides()
    {
        manualOverrides.Clear();
        await RefreshData();
    }

    public async Task AdvanceSimulation()
    {
        if (config.Simulate && simulationData is { Count: > 0 })
        {
            simulationData.RemoveAt(0);
            await RefreshData();
        }
    }

    public async Task ResetSimulation()
    {
        if (config.Simulate && simulationData is { Count: 0 })
        {
            simulationData = null;
            await RefreshData();
        }
    }

    public Task<NewVersionResponse?> GetVersionInfo()
    {
        return Task.FromResult(appVersion);
    }

    public async Task CheckForNewVersion()
    {
        try
        {
            var client = new GitHubClient(new ProductHeaderValue("SolisAgileManager"));

            var newRelease = await client.Repository.Release.GetLatest("webreaper", "SolisAgileManager");
            if (newRelease != null && Version.TryParse(newRelease.TagName, out var newVersion))
            {
                appVersion.NewVersion = newVersion;
                appVersion.NewReleaseName = newRelease.Name;
                appVersion.ReleaseUrl = newRelease.HtmlUrl;

                if( appVersion.UpgradeAvailable )
                    logger.LogInformation("A new version of Damselfly is available: {N}", newRelease.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Unable to check GitHub for latest version: {E}", ex);
        }
    }
}