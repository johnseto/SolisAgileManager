namespace SolisManager.Extensions;

public static class EnergyExtensions
{
    public static IEnumerable<(DateTime start, decimal energyKWH)> ConvertPowerDataTo30MinEnergy<T>(this IEnumerable<T> source,
        Func<T, (DateTime startTime, decimal powerKW)> GetDataFunc)
    {
        var rawData = source.Select(GetDataFunc)
            .OrderBy(x => x.startTime)
            .ToList();

        if (rawData.Count <= 1 )
            return [];

        var results = new Dictionary<DateTime, decimal>();
        var start = rawData.First();
        decimal energyCarryOver = 0;
        
        foreach (var end in rawData.Skip(1))
        {
            var thirtyMinSlotStart = new DateTime(start.startTime.Year, start.startTime.Month, start.startTime.Day, start.startTime.Hour, 
                start.startTime.Minute - (start.startTime.Minute % 30), 0);

            if (!results.TryGetValue(thirtyMinSlotStart, out var existingRecord))
                results[thirtyMinSlotStart] = 0;
            
            // Calculate the average rate between the start and end of the forecast slot
            var minutes = (decimal)(end.startTime - start.startTime).TotalMinutes;
            var hourRatio = minutes / 60.0M;
            var averagePowerkW = (start.powerKW + end.powerKW) / 2.0M;
            var periodEnergyKWH = averagePowerkW * hourRatio;

            // TODO: If the data crosses the boundary of a 30-minute slot, we need to:
            //   a) Reduce the number of minutes by the number that fall before the
            //      start of the 30-minute slot
            //   b) Carry-over the energy for the overlap that falls after the end of
            //      the 30-minute slot.
            if (periodEnergyKWH > 0)
            {
                var outsideSlotMins = (end.startTime - thirtyMinSlotStart.AddMinutes(30)).TotalMinutes;

                if (outsideSlotMins > 0)
                {
                    // So say we have 3 minutes before, 
                    decimal carryOverRatio = (decimal)outsideSlotMins / minutes;
                    // Store the carry-over for next time
                    energyCarryOver = periodEnergyKWH * carryOverRatio;
                    // Reduce the energy for this first slot
                    periodEnergyKWH -= energyCarryOver;
                }
                else
                {
                    periodEnergyKWH += energyCarryOver;
                    energyCarryOver = 0;
                }
            }

            // Add it to the slot total
            results[thirtyMinSlotStart] += periodEnergyKWH;
            
            start = end;
        }

        var output = results.Select(x => (x.Key, x.Value))
                      .OrderBy(x => x.Key).ToList();
        return output;
    }
}