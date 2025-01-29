using System.Diagnostics;
using System.Globalization;
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

    private readonly List<HistoryEntry> executionHistory = [];
    private const string executionHistoryFile = "SolisManagerExecutionHistory.csv";
    private NewVersionResponse appVersion = new();
    private List<OctopusPriceSlot>? simulationData;
    
    
    private async Task EnrichWithSolcastData(IEnumerable<OctopusPriceSlot>? slots)
    {
        var solcast = await solcastApi.GetSolcastForecast();
        
        if (solcast.forecasts == null || !solcast.forecasts.Any())
            return;
        
        InverterState.ForecastDayLabel = "today";
        
        // THe forecast is the sum of all slot forecasts for the day, offset by the damping factor
        var forecast = solcast.forecasts?.Where(x => x.PeriodStart.Date == DateTime.Today)
                                                 .Sum(x => x.ForecastkWh * config.SolcastDampFactor);

        InverterState.ForecastPVkWh = forecast;
        InverterState.SolcastTimeStamp = solcast.lastApiUpdate;

        if (slots != null && slots.Any() && solcast.forecasts != null)
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

        // Save the overrides
        var overrides = GetExistingSlotOverrides();
        
        // Our working set
        IEnumerable<OctopusPriceSlot> slots;
        
        if (config.Simulate && simulationData != null)
        {
            slots = simulationData;
        }
        else
        {
            var lastSlot = InverterState.Prices?.MaxBy(x => x.valid_from);
            
            logger.LogTrace("Refreshing data...");

            var octRatesTask = octopusAPI.GetOctopusRates(config.OctopusProductCode);

            await Task.WhenAll(RefreshBatteryState(), octRatesTask, LoadExecutionHistory());
            
            await CalculateForecastWeightings(executionHistory);

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
        }

        // Now reapply
        ApplyPreviousOverrides(slots, overrides);
        
        await EnrichWithSolcastData(slots);
        
        var processedSlots = EvaluateSlotActions(slots.ToArray());

        // Update the state
        InverterState.Prices = processedSlots;

        await ExecuteSlotChanges(processedSlots);
        
        if( config.Simulate && simulationData == null )
            simulationData = InverterState.Prices.ToList();
    }

    private IEnumerable<ChangeSlotActionRequest> GetExistingSlotOverrides()
    {
        return InverterState.Prices
            .Where(x => x.ManualOverrideAction != null)
            .Select(x => new ChangeSlotActionRequest
            {
                SlotStart = x.valid_from,
                NewAction = x.ManualOverrideAction!.Value
            });
    }

    private void ApplyPreviousOverrides(IEnumerable<OctopusPriceSlot> slots, IEnumerable<ChangeSlotActionRequest> overrides)
    {
        var lookup = overrides.ToDictionary(x => x.SlotStart);
        foreach (var slot in slots)
        {
            if (lookup.TryGetValue(slot.valid_from, out var overRide))
                slot.ManualOverrideAction = overRide.NewAction;
            else
                slot.ManualOverrideAction = null;
        }
    }
    
    private async Task ExecuteSlotChanges(IEnumerable<OctopusPriceSlot> slots)
    {
        var firstSlot = slots.FirstOrDefault();
        if (firstSlot != null)
        {
            if (!config.Simulate)
                await AddToExecutionHistory(firstSlot);

            var matchedSlots = slots.TakeWhile(x => x.ActionToExecute == firstSlot.ActionToExecute).ToList();

            if (matchedSlots.Any())
            {
                logger.LogDebug("Found {N} slots with matching action to conflate", matchedSlots.Count);

                // The timespan is from the start of the first slot, to the end of the last slot.
                var start = matchedSlots.First().valid_from;
                var end = matchedSlots.Last().valid_to;

                if (firstSlot.ActionToExecute == SlotAction.Charge)
                {
                    await solisApi.SetCharge(start, end, null, null, config.Simulate);
                }
                else if (firstSlot.ActionToExecute == SlotAction.Discharge)
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
            // First, reset all the slot states
            foreach (var slot in slots)
                slot.PlanAction = SlotAction.DoNothing;
            
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
                    slot.PlanAction = SlotAction.Charge;
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
                    slot.PlanAction = SlotAction.ChargeIfLowBattery;
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
                        slot.PlanAction = SlotAction.DoNothing;
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
                        prePeakSlot.PlanAction = SlotAction.Charge;
                        prePeakSlot.ActionReason = $"Cheaper slot to ensure battery is charged to {config.PeakPeriodBatteryUse:P0} before the peak period";
                    }
                }
            }

            // If there are any slots below our "Blimey it's cheap" threshold, elect to charge them anyway.
            foreach (var slot in slots.Where(s => s.value_inc_vat < config.AlwaysChargeBelowPrice))
            {
                slot.PriceType = PriceType.BelowThreshold;
                slot.PlanAction = SlotAction.Charge;
                slot.ActionReason =
                    $"Price is below the threshold of {config.AlwaysChargeBelowPrice}p/kWh, so always charge";
            }

            foreach (var slot in slots.Where(s => s.value_inc_vat < 0))
            {
                slot.PriceType = PriceType.Negative;
                slot.PlanAction = SlotAction.Charge;
                slot.ActionReason = "Negative price - always charge";
            }

            // For any slots that are set to "charge if low battery", update them to 'charge' if the 
            // battery SOC is, indeed, low. Only do this for enough slots to fully charge the battery.
            if (InverterState.BatterySOC < config.LowBatteryPercentage)
            {
                foreach (var slot in slots.Where(x => x.PlanAction == SlotAction.ChargeIfLowBattery)
                             .Take(config.SlotsForFullBatteryCharge))
                {
                    slot.PlanAction = SlotAction.Charge;
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
                        x.PlanAction = SlotAction.Discharge;
                        x.ActionReason =
                            "Contiguous negative slots allow the battery to be discharged and charged again.";
                    });
                }
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

    private async Task<bool> UpdateConfigWithOctopusTariff(SolisManagerConfig theConfig)
    {
        if (!string.IsNullOrEmpty(theConfig.OctopusAPIKey) && !string.IsNullOrEmpty(theConfig.OctopusAccountNumber))
        {
            var productCode =
                await octopusAPI.GetCurrentOctopusTariffCode(theConfig.OctopusAPIKey, theConfig.OctopusAccountNumber);

            if (!string.IsNullOrEmpty(productCode))
            {
                if (theConfig.OctopusProductCode != productCode)
                    logger.LogInformation("Octopus product code has changed: {Old} => {New}", theConfig.OctopusProductCode, productCode);

                theConfig.OctopusProductCode = productCode;
                return true;
            }
        }

        return false;
    }

    public async Task RefreshTariff()
    {
        if (!string.IsNullOrEmpty(config.OctopusAPIKey) && !string.IsNullOrEmpty(config.OctopusAccountNumber))
        {
            logger.LogDebug("Executing Tariff Refresh scheduler");
            await UpdateConfigWithOctopusTariff(config);
        }
    }

    public async Task UpdateInverterTime()
    {
        if (config.AutoAdjustInverterTime)
        {
            await solisApi.UpdateInverterTime();
        }
    }

    public async Task UpdateInverterDayData()
    {
        for (int i = 0; i < 7; i++)
        {
            // Call this to prime the cache with the last 7 days' inverter data
            await solisApi.GetInverterDay(i);
        }
    }

    public Task<List<HistoryEntry>> GetHistory()
    {
        return Task.FromResult(executionHistory);
    }

    public Task<SolisManagerConfig> GetConfig()
    {
        return Task.FromResult(config);
    }

    public async Task<ConfigSaveResponse> SaveConfig(SolisManagerConfig newConfig)
    {
        logger.LogInformation("Saving config to server...");

        var siteIds = SolcastAPI.GetSolcastSites(newConfig.SolcastSiteIdentifier);

        if (siteIds.Length > 2)
        {
            return new ConfigSaveResponse
            {
                Success = false,
                Message = "A maximum of two Solcast site IDs can be specified"
            };
        }
        
        if (siteIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != siteIds.Length)
        {
            return new ConfigSaveResponse
            {
                Success = false,
                Message = "Each specified Site ID must be unique"
            };
        }
        
        if (!string.IsNullOrEmpty(newConfig.OctopusAPIKey) && !string.IsNullOrEmpty(newConfig.OctopusAccountNumber))
        {
            try
            {
                var result = await UpdateConfigWithOctopusTariff(newConfig);

                if (!result)
                    throw new InvalidOperationException("Could not get product code");
            }
            catch(Exception ex)
            {
                logger.LogError(ex, "Octopus account lookup failed");
                return new ConfigSaveResponse
                {
                    Success = false,
                    Message = "Unable to get tariff details from Octopus. Please check your account and API key."
                };
            }
        }
        newConfig.CopyPropertiesTo(config);
        await config.SaveToFile(Program.ConfigFolder);
        await RefreshData();
        
        return new ConfigSaveResponse{ Success = true };
    }

    public async Task OverrideSlotAction(ChangeSlotActionRequest change)
    {
        logger.LogInformation("Updating slot action for {S} to {A}...", change.SlotStart, change.NewAction);
        await SetManualOverrides([change]);
    }

    private int NearestHalfHour(int minute) => minute - (minute % 30);

    private IEnumerable<ChangeSlotActionRequest> CreateOverrides(DateTime start, SlotAction action, int slotCount)
    {
        var currentSlot =  new DateTime(start.Year, start.Month, start.Day, start.Hour, NearestHalfHour(start.Minute), 0);

        List<ChangeSlotActionRequest> overrides = new();
        
        foreach (var slot in  Enumerable.Range(0, slotCount))
        {
            yield return new ChangeSlotActionRequest
            {
                NewAction = action,
                SlotStart = currentSlot
            };

            currentSlot = currentSlot.AddMinutes(30);
        }
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

        var overrides = CreateOverrides(DateTime.UtcNow, SlotAction.Charge, slotsRequired).ToList();
        await SetManualOverrides(overrides);
    }

    public async Task DischargeBattery()
    {
        var overrides = CreateOverrides(DateTime.UtcNow, SlotAction.Discharge, CalculateDischargeSlots()).ToList();
        await SetManualOverrides(overrides);
    }

    private int CalculateDischargeSlots()
    {
        double slotsRequired = config.SlotsForFullBatteryCharge * (InverterState.BatterySOC / 100.0);
        return (int)Math.Round(slotsRequired, MidpointRounding.ToPositiveInfinity);
    }

    public async Task DumpAndChargeBattery()
    {
        var discharge = CreateOverrides(DateTime.UtcNow, SlotAction.Discharge, CalculateDischargeSlots()).ToList();
        var lastDischarge = discharge.Last().SlotStart.AddMinutes(30);
        var charge = CreateOverrides(lastDischarge, SlotAction.Charge, config.SlotsForFullBatteryCharge);
        await SetManualOverrides(discharge.Concat(charge).ToList());
    }

    private async Task SetManualOverrides(List<ChangeSlotActionRequest> overrides)
    {
        var lookup = InverterState.Prices.ToDictionary(x => x.valid_from);

        foreach (var overRide in overrides)
        {
            if (lookup.TryGetValue(overRide.SlotStart, out var slot))
            {
                if (slot.PlanAction == overRide.NewAction)
                {
                    // Clear the existing override
                    slot.ManualOverrideAction = null;
                    logger.LogInformation("Cleared override: {S}", overRide);
                    continue;
                }

                // Set the override
                slot.ManualOverrideAction = overRide.NewAction;
                logger.LogInformation("Set override: {S}", overRide);
            }
        }

        await RefreshData();
    }
    
    public async Task ClearOverrides()
    {
        foreach( var slot in InverterState.Prices)
            slot.ManualOverrideAction = null;
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

    public Task<NewVersionResponse> GetVersionInfo()
    {
        return Task.FromResult(appVersion);
    }

    public async Task<OctopusProductResponse?> GetOctopusProducts()
    {
        return await octopusAPI.GetOctopusProducts();
    }

    public async Task<OctopusTariffResponse?> GetOctopusTariffs(string product)
    {
        return await octopusAPI.GetOctopusTariffs(product);
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

    public async Task<TariffComparison> GetTariffComparisonData(string tariffA, string tariffB)
    {
        logger.LogInformation("Running comparison for {A} vs {B}...", tariffA, tariffB);

        var ratesATask = octopusAPI.GetOctopusRates(tariffA);
        var ratesBTask = octopusAPI.GetOctopusRates(tariffB);

        await Task.WhenAll(ratesATask, ratesBTask);
        
        return new TariffComparison
        {
            TariffA = tariffA,
            TariffAPrices = await ratesATask,
            TariffB = tariffB,
            TariffBPrices = await ratesBTask
        };
    }

    public async Task CalculateForecastWeightings(IEnumerable<HistoryEntry> forecastHistory)
    {
        // Not quite ready for this yet...
        if (!Debugger.IsAttached)
            return;
        
        var history = new List<InverterDayRecord>();

        for (int i = 0; i < 7; i++)
        {
            var result = await solisApi.GetInverterDay(i);

            if( result != null)
                history.AddRange(result.data);
        }

        var processed = new List<(DateTime start, decimal powerKW)>();
        
        foreach (var record in history)
        {
            if (DateTime.TryParseExact(record.timeStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                processed.Add( (date, record.pac / 1000));
            }
        }
        
        var powerSlots = processed.ConvertPowerDataTo30MinEnergy(x => (x.start, x.powerKW));

        var powerPerDay = powerSlots.GroupBy(x => x.start.Date)
                .Select(x => new {Date =x.Key, Energy = x.Sum(v => v.energyKWH) })
                .ToList();

        foreach( var d in powerPerDay )
        {
            logger.LogInformation("PV yield {D:dd-MMM-yyyy} = {Y:F2} kWh", d.Date, d.Energy);
        }
        
        // Now iterate through the historic forecasts, and compare them
        var prevForecast = forecastHistory
            .Where(x => x.Start > DateTime.UtcNow.AddDays(-7))
            .DistinctBy(x => x.Start)
            .ToDictionary(x => x.Start, x => x.ForecastKWH);

        foreach (var day in powerPerDay)
        {
            if (prevForecast.TryGetValue(day.Date, out var forecast))
            {
                if (forecast == 0)
                    continue;
                
                var percentage =  day.Energy / forecast;
                logger.LogInformation("{D:dd-MMM HH:mm}, forecast = {F:F2}kWh, actual = {A:F2}kWh, percentage = {P:P1}",
                                day.Date, forecast, day.Energy, percentage);
            }
        }
    }

    private record SlotTotals
    {
        public List<InverterDayRecord> day { get; init; } = new();
        public decimal totalPowerKWH { get; set; }
    }
}