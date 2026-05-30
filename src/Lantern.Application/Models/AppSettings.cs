namespace Lantern.Application;

public sealed class AppSettings
{
    public bool NotificationsEnabled { get; set; } = true;
    public bool? DarkModeEnabled { get; set; }
    public bool AutoScanEnabled { get; set; } = true;
    public int AutoScanIntervalMinutes { get; set; } = 15;
    public ScanProfile ScanProfile { get; set; } = ScanProfile.Home;
    public bool CompanionDashboardEnabled { get; set; }
    public int CompanionDashboardPort { get; set; } = 5274;
    public string VendorCsvPath { get; set; } = string.Empty;
    public bool MikroTikEnabled { get; set; }
    public string MikroTikHost { get; set; } = string.Empty;
    public string MikroTikUsername { get; set; } = string.Empty;
    public string MikroTikPassword { get; set; } = string.Empty;
}
