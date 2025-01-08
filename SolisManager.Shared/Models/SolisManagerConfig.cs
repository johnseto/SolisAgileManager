using System.Reflection;
using System.Text.Json;

namespace SolisManager.Shared.Models;

public record SolisManagerConfig
{
    private static string settingsFileName = "SolisManagerConfig.json";
    
    public string SolisAPIKey { get; set; }
    public string SolisAPISecret { get; set; }
    public string SolisInverterSerial { get; set; }
    public string OctopusProduct { get; set; }
    public string OctopusProductCode { get; set; }
    public int SlotsForFullBatteryCharge { get; set; }
    public int AlwaysChargeBelowPrice { get; set; } = 10;
    public int LowBatteryPercentage { get; set; } = 25;
    public int MaxChargeRateAmps { get; set; } = 50;
    
    public string SolcastAPIKey { get; set; }
    public string SolcastSiteIdentifier { get; set; }
    public bool Simulate { get; set; } = true;

    public async Task SaveToFile()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(settingsFileName, json);
    }
    
    public bool ReadFromFile()
    {
        if (File.Exists(settingsFileName))
        {
            var content = File.ReadAllText(settingsFileName);
            var settings = JsonSerializer.Deserialize<SolisManagerConfig>(content);
            
            settings.CopyPropertiesTo(this);
            return true;
        }

        return false;
    }

    public bool IsValid()
    {
        if (string.IsNullOrEmpty(SolisAPIKey)) return false;
        if (string.IsNullOrEmpty(SolisAPISecret)) return false;
        if (string.IsNullOrEmpty(SolisInverterSerial)) return false;

        if (string.IsNullOrEmpty(OctopusProduct)) return false;
        if (string.IsNullOrEmpty(OctopusProductCode)) return false;

        return true;
    }
}