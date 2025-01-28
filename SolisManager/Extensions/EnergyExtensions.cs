namespace SolisManager.Extensions;

public static class EnergyExtensions
{
    public static IEnumerable<(DateTime start, decimal energyKWH)> ConvertPowerDataTo30MinEnergy<T>(this IEnumerable<T> source,
        Func<T, (DateTime time, decimal powerKW)> GetDataFunc)
    {
        if (source.Count() <= 1 )
            return [];

        var results = new Dictionary<DateTime, decimal>();
        var prevForecast = source.First();
        
        foreach (var item in source.Skip(1))
        {
            var prevData = GetDataFunc(prevForecast);
            var data = GetDataFunc(item);
            
            var thirtyMinuteSlot = new DateTime(data.time.Year, data.time.Month, data.time.Day, data.time.Hour, 
                data.time.Minute - (data.time.Minute % 30), 0);

            if (!results.TryGetValue(thirtyMinuteSlot, out var existingRecord))
                results[thirtyMinuteSlot] = 0;
            
            // Calculate the average rate between the start and end of the forecast slot
            var minutes = (decimal)(data.time - prevData.time).TotalMinutes;
            var ratio = minutes / 60.0M;
            var averagePowerkW = (prevData.powerKW + data.powerKW) / 2.0M;
            var periodEnergyKWH = averagePowerkW * ratio;

            // Add it to the slot total
            results[thirtyMinuteSlot] += periodEnergyKWH;

            prevForecast = item;
        }

        var output = results.Select(x => (x.Key, x.Value))
                      .OrderBy(x => x.Key).ToList();
        return output;
    }
}