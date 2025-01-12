namespace SolisManager.Shared.Models;

public static class ModelExtensions
{
    public static string Description(this SlotAction action) => action switch
    {
        SlotAction.Charge => "Charge",
        SlotAction.Discharge => "Discharge",
        SlotAction.DoNothing => "No Action",
        SlotAction.ChargeIfLowBattery => "Boost",
        _ => string.Empty
    };
}