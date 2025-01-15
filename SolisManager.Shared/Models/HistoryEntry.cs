using System.Globalization;

namespace SolisManager.Shared.Models;

public class HistoryEntry
{
    const string dateFormat = "dd-MMM-yyyy HH:mm";

    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public decimal Price { get; set; }
    public int BatterySOC { get; set; }
    public PriceType Type { get; set; }
    public SlotAction Action { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal ForecastKWH { get; set; }    
    public decimal ActualKWH { get; set; }    

    public HistoryEntry() { }

    public HistoryEntry(OctopusPriceSlot slot, int batterySOC)
    {
        Start = slot.valid_from;
        End = slot.valid_to;
        Price = slot.value_inc_vat;
        BatterySOC = batterySOC;
        Type = slot.PriceType;
        Action = slot.Action;
        ActualKWH = 0; // Todo
        ForecastKWH = slot.pv_est_kwh ?? 0;
        Reason = slot.ActionReason;
    }

    public override string ToString()
    {
        return $"{Start:dd-MMM HH:mm]} - {Price}p/kWh, Battery: {BatterySOC:P}";
    }

    public static HistoryEntry? TryParse(string logLine)
    {
        var entry = new HistoryEntry();

        try
        {
            var parts = logLine.Split(",", 9, StringSplitOptions.TrimEntries);

            entry.Start = DateTime.ParseExact(parts[0], dateFormat, CultureInfo.InvariantCulture);
            entry.End = DateTime.ParseExact(parts[1], dateFormat, CultureInfo.InvariantCulture);
            entry.Price = decimal.Parse(parts[2], CultureInfo.InvariantCulture);
            entry.Action = Enum.Parse<SlotAction>(parts[3], true);
            entry.Type = Enum.Parse<PriceType>(parts[4], true);
            entry.BatterySOC = int.Parse(parts[5].Replace("%", ""));
            entry.ActualKWH = decimal.Parse(parts[6], CultureInfo.InvariantCulture);
            entry.ForecastKWH = decimal.Parse(parts[7], CultureInfo.InvariantCulture);
            entry.Reason = parts[9].Trim('\"');
        }
        catch
        {
            var parts = logLine.Split(",", 7, StringSplitOptions.TrimEntries);

            entry.Start = DateTime.ParseExact(parts[0], dateFormat, CultureInfo.InvariantCulture);
            entry.End = DateTime.ParseExact(parts[1], dateFormat, CultureInfo.InvariantCulture);
            entry.Price = decimal.Parse(parts[2], CultureInfo.InvariantCulture);
            entry.Action = Enum.Parse<SlotAction>(parts[3], true);
            entry.Type = Enum.Parse<PriceType>(parts[4], true);
            entry.BatterySOC = int.Parse(parts[5].Replace("%", ""));
            entry.Reason = parts[6].Trim('\"');
        }

        return entry;
    }
    
    public string GetAsCSV()
    {
        return string.Join(", ", 
            Start.ToString(dateFormat),
            End.ToString(dateFormat),
            Price.ToString("0.00"),
            Action.ToString(), 
            Type.ToString(),
            BatterySOC.ToString() + '%',
            ActualKWH.ToString("0.00"),
            ForecastKWH.ToString("0.00"),
            $"\"{Reason}\""
        );
    }
}