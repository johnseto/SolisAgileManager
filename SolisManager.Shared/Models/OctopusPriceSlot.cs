using Humanizer;

namespace SolisManager.Shared.Models;

public enum PriceType
{
    Average = 0,
    Cheapest,
    BelowThreshold,
    BelowAverage,
    Dropping,
    MostExpensive
}

public enum SlotAction
{
    DoNothing,
    Charge,
    ChargeIfLowBattery,
    Discharge
}

public record OctopusPriceSlot
{
    public decimal value_exc_vat { get; set;  }
    public decimal value_inc_vat { get; set;  }
    public DateTime valid_from { get; set;  }
    public DateTime valid_to { get; set;  }
    public PriceType PriceType { get; set; }
    public SlotAction Action { get; set; } = SlotAction.DoNothing;
    public string ActionReason { get; set; } = string.Empty;
    public Guid Id { get; set; } = Guid.NewGuid();
    public decimal? pv_est_kwh { get; set; }

    public override string ToString()
    {
        return $"{valid_from:dd-MMM-yyyy HH:mm}-{valid_to:HH:mm}: {Action.Humanize()} (price: {value_inc_vat}p/kWh, Reason: {ActionReason})";
    }
}
