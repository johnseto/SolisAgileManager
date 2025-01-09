namespace SolisManager.Shared.Models;

public class SolisManagerState
{
    public DateTime TimeStamp { get; set; }
    public IEnumerable<OctopusPriceSlot> Prices { get; set; } = [];
    public int BatterySOC { get; set; }
    public decimal TodayPVkWh { get; set; }
    public decimal CurrentPVkW { get; set; }
    public DateTime BatteryTimeStamp { get; set; }
    public DateTime? SolcastTimeStamp { get; set; }
}