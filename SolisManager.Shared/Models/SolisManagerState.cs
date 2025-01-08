namespace SolisManager.Shared.Models;

public class SolisManagerState
{
    public DateTime TimeStamp { get; set; }
    public IEnumerable<OctopusPriceSlot> Prices { get; set; } = [];
    public int BatterySOC { get; set; }
}