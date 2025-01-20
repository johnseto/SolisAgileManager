namespace SolisManager.Shared.Models;

public record ChangeSlotActionRequest
{
    public OctopusPriceSlot Slot { get; init; }
    public SlotAction NewAction { get; init; }
}