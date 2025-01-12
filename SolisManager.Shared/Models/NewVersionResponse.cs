namespace SolisManager.Shared.Models;

public class NewVersionResponse
{
    public Version? CurrentVersion { get; set; }
    public Version? NewVersion { get; set; }
    public string? NewReleaseName { get; set; }
    public string? ReleaseUrl { get; set; }

    public bool UpgradeAvailable()
    {
        if (CurrentVersion == null)
            return false;

        if (CurrentVersion.Major == 1 && 
            CurrentVersion.Minor == 0 &&
            CurrentVersion.Build == 0 &&
             CurrentVersion.Revision == 0)
        {
            // Dev build, so don't warn about new versions.
            return false;
        }
        
        if (NewVersion != null)
        {
            return NewVersion > CurrentVersion;
        }

        return false;
    }
}