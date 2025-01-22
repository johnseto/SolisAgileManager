namespace SolisManager.Shared.Models;

public class TariffComparison
{
    public string TariffA { get; set; } = string.Empty;
    public IEnumerable<OctopusPriceSlot> TariffAPrices { get; set; } = [];

    public string TariffB { get; set; } = string.Empty;
    public IEnumerable<OctopusPriceSlot> TariffBPrices { get; set; } = [];
}