namespace SolisManager.Shared.Models;

public record ChangeSlotActionRequest
{
    public DateTime SlotStart { get; set; }
    public SlotAction NewAction { get; init; }

    public override string ToString()
    {
        return $"{SlotStart:HH:mm} - {NewAction}";
    }
}