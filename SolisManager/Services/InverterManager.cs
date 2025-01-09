using Coravel.Invocable;
using SolisManager.APIWrappers;
using SolisManager.Shared;
using SolisManager.Shared.Models;

namespace SolisManager.Services;

public class InverterManager(SolisManagerConfig config, 
                            OctopusAPI octopusAPI,
                            SolisAPI solisApi,
                            SolcastAPI solcastApi,
                            ILogger<InverterManager> logger) : IInverterService, IInverterRefreshService
{
    public SolisManagerState InverterState { get; } = new();
    private readonly List<OctopusPriceSlot> manualOverrides = new();
    private readonly List<HistoryEntry> executionHistory = new();
    private const string executionHistoryFile = "SolisManagerExecutionHistory.csv";

    private async Task EnrichSlotsFromSolcast(IEnumerable<OctopusPriceSlot> slots)
    {
        var forecast = await solcastApi.GetSolcastForecast();
        var lookup = forecast.ToDictionary(x => x.period_end);
        InverterState.SolcastTimeStamp = null;

        foreach (var slot in slots)
        {
            if (lookup.TryGetValue(slot.valid_to, out var solcastEstimate))
            {
                // Estimate is in kW. Since slots are 30 mins, divide by 2 to get kWh.
                slot.pv_est_kwh = (solcastEstimate.pv_estimate / 2.0M);
                InverterState.SolcastTimeStamp = DateTime.UtcNow;
            }
            else
            {
                // No data
                slot.pv_est_kwh = null;
            }
        }
    }

    private async Task AddToExecutionHistory( OctopusPriceSlot slot )
    {
        try
        {
            if (!executionHistory.Any() && File.Exists(executionHistoryFile))
            {
                var lines = await File.ReadAllLinesAsync(executionHistoryFile);
                logger.LogInformation("Loaded {C} entries from execution history file {F}", lines.Length,
                    executionHistoryFile);

                // Limit to 1440 items. At 48 slots per day, that gives us 30 days of history. 
                var entries = lines.TakeLast(1440)
                    .Select(x => HistoryEntry.TryParse(x))
                    .Where(x => x != null)
                    .Select(x => x!)
                    .ToList();

                executionHistory.AddRange(entries);
            }
            
            var newEntry = new HistoryEntry(slot, InverterState.BatterySOC);
            
            var lastEntry = executionHistory.LastOrDefault();

            if (lastEntry == null || lastEntry.Start != newEntry.Start)
            {
                // Add the item
                executionHistory.Add(newEntry);

                // And write
                await File.WriteAllLinesAsync(executionHistoryFile, executionHistory.Select(x => x.GetAsCSV()));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add entry to execution history");
        }
    }

    private async Task RefreshData()
    {
        logger.LogTrace("Refreshing data...");

        var octRatesTask = octopusAPI.GetOctopusRates();

        await Task.WhenAll(RefreshBatteryState(), octRatesTask);

        // Stamp the last time we did an update
        InverterState.TimeStamp = DateTime.UtcNow;

        // Now, process the octopus rates
        var slots = await octRatesTask;

        InverterState.Prices = EvaluateSlotActions(slots.OrderBy(x => x.valid_from).ToArray());

        var firstSlot = InverterState.Prices.FirstOrDefault();
        if (firstSlot != null)
        {
            var now = DateTime.UtcNow;

            // Do we care if we run this multiple times?!
            // if ( firstSlot.valid_from <= now && firstSlot.valid_to >= now )
            {
                logger.LogInformation("Execute action for slot: {E} (Simulate: {S}", firstSlot, config.Simulate);

                await AddToExecutionHistory(firstSlot);

                if (firstSlot.Action == SlotAction.Charge)
                {
                    await solisApi.SetCharge(firstSlot.valid_from, firstSlot.valid_to, true, config.Simulate);
                }
                else if (firstSlot.Action == SlotAction.Discharge)
                {
                    await solisApi.SetCharge(firstSlot.valid_from, firstSlot.valid_to, false, config.Simulate);
                }
                else
                {
                    var midnight = DateTime.UtcNow.Date;
                    // And the discharge slot.
                    await solisApi.SetCharge(midnight, midnight, false, config.Simulate);
                    // Clear the charging slot
                    await solisApi.SetCharge(midnight, midnight, true, config.Simulate);
                }
            }
        }
    }

    private IEnumerable<OctopusPriceSlot> EvaluateSlotActions(OctopusPriceSlot[]? slots)
    {
        if (slots == null)
            return [];

        logger.LogTrace("Evaluating slot actions...");

        try
        {
            OctopusPriceSlot[]? cheapestSlots = null;
            OctopusPriceSlot[]? priciestSlots = null;

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

            // Similar calculation for the peak period.
            int peakPeriodLength = 7; // Peak period is usually 4pm - 7:30pm, so 7 slots.
            for (var i = 0; i <= slots.Length - peakPeriodLength; i++)
            {
                var peakPeriod = slots[i .. (i + peakPeriodLength)];
                var peakPeriodTotal = peakPeriod.Sum(x => x.value_inc_vat);

                if (priciestSlots == null || peakPeriodTotal > priciestSlots.Sum(x => x.value_inc_vat))
                    priciestSlots = peakPeriod;
            }

            if (cheapestSlots != null)
            {
                foreach (var slot in cheapestSlots)
                {
                    slot.PriceType = PriceType.Cheapest;
                    slot.Action = SlotAction.Charge;
                    slot.ActionReason = "This is the cheapest set of slots, to fully charge the battery";
                }
            }

            if (priciestSlots != null)
            {
                foreach (var slot in priciestSlots)
                {
                    slot.PriceType = PriceType.MostExpensive;
                    slot.ActionReason = "Avoiding charging due to peak prices.";
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
                var averagePrice = averagePriceSlots.Average(x => x.value_inc_vat);
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
                // If we have a set of priciest slots, we want to charge before them. So 
                // work backwards from the first, applying a charge instruction on each
                // slot before the priciest, until we've got enough slots to fully charge
                // the battery. Note that we skip an extra one, because the slot before
                // the priciest ones is always a bit more expensive too.
                // Just in case it's REALLY expensive, allow ourselves a couple of steps
                // back to look for slightly cheaper rates. Can't go back too far though
                // otherwise the battery might not last through the peak period.
                var prePeakSlot = Array.IndexOf(slots, priciestSlots.First()) - 1;
                var extraStepsBack = 3;
                var chargeSlotsNeeeded = config.SlotsForFullBatteryCharge;

                while (prePeakSlot >= 0)
                {
                    var slotTocheck = slots[prePeakSlot];
                    // TODO: Make this less of an arbitrary threshold - https://github.com/Webreaper/SolisAgileManager/issues/7
                    if( slotTocheck.value_inc_vat > 50)
                    {
                        // It's a bit pricey. See if we're allowed to look 
                        // back a bit further for charging slots.
                        if (extraStepsBack == 0)
                        {
                            // Nope, so just bite the bullet and charge
                            slotTocheck.Action = SlotAction.Charge;
                            slotTocheck.ActionReason = "Cheaper slot to ensure battery is charged before the peak period (earlier slot was cheaper)";
                            chargeSlotsNeeeded--;
                            if (chargeSlotsNeeeded == 0)
                                break;
                        }
                        else
                            extraStepsBack--;
                    }
                    else
                    {
                        // It's expensive, but not terrible. Suck it up and charge
                        slotTocheck.Action = SlotAction.Charge;
                        slotTocheck.ActionReason = "Cheaper slot to ensure battery is charged before the peak period";
                        chargeSlotsNeeeded--;
                        if (chargeSlotsNeeeded == 0)
                            break;
                    }
                    
                    prePeakSlot--;
                }
            }

            // If there are any slots below our "Blimey it's cheap" threshold, elect to charge them anyway.
            foreach (var slot in slots.Where(s => s.value_inc_vat < config.AlwaysChargeBelowPrice))
            {
                slot.PriceType = PriceType.BelowThreshold;
                slot.Action = SlotAction.Charge;
                slot.ActionReason = $"Price is below the threshold of {config.AlwaysChargeBelowPrice}p/kWh, so always charge";
            }

            // For any slots that are set to "charge if low battery", update them to 'charge' if the 
            // battery SOC is, indeed, low. Only do this for enough slots to fully charge the battery.
            if (InverterState.BatterySOC < config.LowBatteryPercentage)
            {
                foreach (var slot in slots.Where(x => x.Action == SlotAction.ChargeIfLowBattery)
                                          .Take(config.SlotsForFullBatteryCharge))
                {
                    slot.Action = SlotAction.Charge;
                    slot.ActionReason = $"Upcoming slot is set to charge if low battery; battery is currently at {InverterState.BatterySOC}%";
                }
            }

            if (manualOverrides.Any())
            {
                var lookup = slots.ToDictionary(x => x.valid_from);

                foreach (var over_ride in manualOverrides)
                {
                    if (lookup.TryGetValue(over_ride.valid_from, out var slot))
                    {
                        slot.Action = over_ride.Action;
                        slot.ActionReason = "Manual override applied.";
                        slot.IsManualOverride = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected exception during slot action evaluation:");
        }

        return slots;
        
    }

    
    public Task RefreshInverterState()
    {
        // Nothing to do on the server side, the refresh is triggered by the scheduler
        return Task.CompletedTask;
    }

    public async Task RefreshBatteryState()
    {
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
        }
    }

    public async Task RefreshSolcastData()
    {
        await EnrichSlotsFromSolcast(InverterState.Prices);
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
        logger.LogTrace("Retrieving config from server...");

        return Task.FromResult(config);
    }

    public async Task SaveConfig(SolisManagerConfig newConfig)
    {
        logger.LogInformation("Saving config to server...");

        newConfig.CopyPropertiesTo(config);
        await config.SaveToFile();
    }

    private int NearestHalfHour(int minute) => minute - (minute % 30);
    private List<OctopusPriceSlot> CreateOverrides(DateTime start, SlotAction action, int slotCount)
    {
        var overrides = Enumerable.Range(0, slotCount)
            .Select(x => new OctopusPriceSlot())
            .ToList();

        var currentSlot = new DateTime(start.Year, start.Month, start.Day, start.Hour, NearestHalfHour(start.Minute), 0);
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
        await solisApi.SetCharge(start, end, true, false);
    }
    
    public async Task ChargeBattery()
    {
        // Work out the percentage charge, and then calculate how many slots it'll take to achieve that
        double percentageToCharge = (100 - InverterState.BatterySOC) / 100.0;
        var slotsRequired = (int)Math.Round(config.SlotsForFullBatteryCharge * percentageToCharge, MidpointRounding.ToPositiveInfinity);
        
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
        manualOverrides.Clear();
        manualOverrides.AddRange(overrides);

        foreach( var slot in overrides)
            logger.LogInformation("Created override: {S}", slot);
        
        await RefreshData();
    }

    public async Task ClearOverrides()
    {
        manualOverrides.Clear();
        await RefreshData();
    }
}