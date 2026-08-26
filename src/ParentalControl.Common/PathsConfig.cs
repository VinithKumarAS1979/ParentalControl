namespace ParentalControl.Common;

/// <summary>Central, well-known locations used by the service and viewer.</summary>
public static class PathsConfig
{
    /// <summary>Folder where daily visited-site logs are written (log-yyyy-MM-dd.txt).</summary>
    public static string LogFolder => @"C:\temp";

    /// <summary>App data folder for configuration and state (blocklist, last-scan state).</summary>
    public static string AppDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ParentalControl");

    public static string BlocklistFile => Path.Combine(AppDataFolder, "blocklist.txt");

    public static string AppBlocklistFile => Path.Combine(AppDataFolder, "app-blocklist.txt");

    public static string StateFile => Path.Combine(AppDataFolder, "scan-state.json");

    public static string LogFileForDate(DateTime date) =>
        Path.Combine(LogFolder, $"log-{date:yyyy-MM-dd}.txt");

    public static void EnsureFoldersExist()
    {
        Directory.CreateDirectory(LogFolder);
        Directory.CreateDirectory(AppDataFolder);
    }
}
