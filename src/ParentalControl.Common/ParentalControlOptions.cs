namespace ParentalControl.Common;

/// <summary>Configuration values consumed by the service and viewer from appsettings.json.</summary>
public sealed class ParentalControlOptions
{
    public string LogFolder { get; set; } = @"C:\temp";
    public string DnsTopologyFolder { get; set; } = @"C:\temp";
    public string AppDataFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ParentalControl");
    public string BlockListFile { get; set; } = string.Empty;
    public string LegacyBlocklistFile { get; set; } = string.Empty;
    public string AppBlocklistFile { get; set; } = string.Empty;
    public string StateFile { get; set; } = string.Empty;
    public int BlocklistApiPort { get; set; } = 8787;
    public int BlockPageHttpPort { get; set; } = 80;
    public int BlockPageHttpsPort { get; set; } = 443;
    public int BrowserRefreshIntervalMinutes { get; set; } = 1;
    public int BlocklistRefreshIntervalMinutes { get; set; } = 1;
    public int DnsTopologyRefreshIntervalMinutes { get; set; } = 1;
    public int BlocklistApiRefreshIntervalMinutes { get; set; } = 1;
    public int AppBlockRefreshIntervalSeconds { get; set; } = 3;

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(DnsTopologyFolder))
        {
            DnsTopologyFolder = LogFolder;
        }

        if (string.IsNullOrWhiteSpace(BlockListFile))
        {
            BlockListFile = Path.Combine(AppDataFolder, "block-list.txt");
        }

        if (string.IsNullOrWhiteSpace(LegacyBlocklistFile))
        {
            LegacyBlocklistFile = Path.Combine(AppDataFolder, "blocklist.txt");
        }

        if (string.IsNullOrWhiteSpace(AppBlocklistFile))
        {
            AppBlocklistFile = Path.Combine(AppDataFolder, "app-blocklist.txt");
        }

        if (string.IsNullOrWhiteSpace(StateFile))
        {
            StateFile = Path.Combine(AppDataFolder, "scan-state.json");
        }
    }
}