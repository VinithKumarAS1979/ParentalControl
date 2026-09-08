namespace ParentalControl.Common;

/// <summary>Central, well-known locations used by the service and viewer.</summary>
public static class PathsConfig
{
    private static ParentalControlOptions _options = new();

    /// <summary>Loads configuration from appsettings and normalizes derived defaults.</summary>
    public static void Initialize(ParentalControlOptions options)
    {
        _options = options;
        _options.Normalize();
    }

    /// <summary>Folder where daily visited-site logs are written (log-yyyy-MM-dd.txt).</summary>
    public static string LogFolder => _options.LogFolder;

    /// <summary>Folder where DNS topology snapshots are written (dns-topology-yyyy-MM-dd.txt).</summary>
    public static string DnsTopologyFolder => _options.DnsTopologyFolder;

    /// <summary>App data folder for configuration and state (blocklist, last-scan state).</summary>
    public static string AppDataFolder => _options.AppDataFolder;

    /// <summary>Canonical block-list file used by the service and browser extension.</summary>
    public static string BlockListFile => _options.BlockListFile;

    /// <summary>Legacy filename kept for compatibility with earlier builds.</summary>
    public static string LegacyBlocklistFile => _options.LegacyBlocklistFile;

    public static string AppBlocklistFile => _options.AppBlocklistFile;

    public static string StateFile => _options.StateFile;

    public static int BlocklistApiPort => _options.BlocklistApiPort;

    public static int BlockPageHttpPort => _options.BlockPageHttpPort;

    public static int BlockPageHttpsPort => _options.BlockPageHttpsPort;

    public static int BrowserRefreshIntervalMinutes => _options.BrowserRefreshIntervalMinutes;

    public static int BlocklistRefreshIntervalMinutes => _options.BlocklistRefreshIntervalMinutes;

    public static int DnsTopologyRefreshIntervalMinutes => _options.DnsTopologyRefreshIntervalMinutes;

    public static int BlocklistApiRefreshIntervalMinutes => _options.BlocklistApiRefreshIntervalMinutes;

    public static string LogFileForDate(DateTime date) =>
        Path.Combine(LogFolder, $"log-{date:yyyy-MM-dd}.txt");

    public static string DnsTopologyLogFileForDate(DateTime date) =>
        Path.Combine(DnsTopologyFolder, $"dns-topology-{date:yyyy-MM-dd}.txt");

    public static void EnsureFoldersExist()
    {
        Directory.CreateDirectory(LogFolder);
        Directory.CreateDirectory(AppDataFolder);
    }
}
