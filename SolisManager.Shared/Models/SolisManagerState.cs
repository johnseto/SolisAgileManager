namespace SolisManager.Shared.Models;

public class SolisManagerState
{
    public DateTime TimeStamp { get; set; }
    public IEnumerable<OctopusPriceSlot> Prices { get; set; } = [];
    public int BatterySOC { get; set; }
    public decimal TodayPVkWh { get; set; }
    public decimal CurrentPVkW { get; set; }
    public decimal HouseLoadkW { get; set; }
    public string StationId { get; set; } = string.Empty;
    public DateTime BatteryTimeStamp { get; set; }
    public DateTime? SolcastTimeStamp { get; set; }
}