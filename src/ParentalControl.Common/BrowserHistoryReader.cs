using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ParentalControl.Common.Models;

namespace ParentalControl.Common;

/// <summary>Reads locally stored browser history (Chrome, Edge, Firefox) from every Windows
/// user profile on the machine, not just the account the service happens to run as.
/// History databases are copied to a temp file first because the browser keeps an exclusive lock on them while running.</summary>
public sealed class BrowserHistoryReader(ILogger<BrowserHistoryReader> logger)
{
    // Profile folders under C:\Users that are not real user accounts and never contain browser data.
    private static readonly string[] NonUserProfileNames =
        ["Public", "Default", "Default User", "All Users", "defaultuser0", "WDAGUtilityAccount"];

    public IReadOnlyList<(string SourceKey, string WindowsUser, string Browser, string DbPath)> DiscoverProfiles()
    {
        var results = new List<(string, string, string, string)>();
        var usersRoot = Path.GetPathRoot(Environment.SystemDirectory) is { } root
            ? Path.Combine(root, "Users")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "..");

        if (!Directory.Exists(usersRoot))
        {
            // Fall back to just the current user if C:\Users isn't discoverable for some reason.
            AddProfilesForUser(results, Environment.UserName, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            return results;
        }

        foreach (var userDir in Directory.EnumerateDirectories(usersRoot))
        {
            var userName = Path.GetFileName(userDir);
            if (NonUserProfileNames.Contains(userName, StringComparer.OrdinalIgnoreCase)) continue;

            var localAppData = Path.Combine(userDir, "AppData", "Local");
            var roamingAppData = Path.Combine(userDir, "AppData", "Roaming");

            try
            {
                AddProfilesForUser(results, userName, localAppData, roamingAppData);
            }
            catch (UnauthorizedAccessException)
            {
                logger.LogWarning("No permission to read browser profiles for user '{User}' - service must run as an administrator/SYSTEM to scan other accounts", userName);
            }
        }

        return results;
    }

    private static void AddProfilesForUser(List<(string, string, string, string)> results, string userName, string localAppData, string roamingAppData)
    {
        AddChromiumProfiles(results, userName, "Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data"));
        AddChromiumProfiles(results, userName, "Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data"));
        AddFirefoxProfiles(results, userName, Path.Combine(roamingAppData, "Mozilla", "Firefox", "Profiles"));
    }

    private static void AddChromiumProfiles(List<(string, string, string, string)> results, string userName, string browser, string userDataDir)
    {
        if (!Directory.Exists(userDataDir)) return;

        foreach (var profileDir in Directory.EnumerateDirectories(userDataDir))
        {
            var historyDb = Path.Combine(profileDir, "History");
            if (File.Exists(historyDb))
            {
                results.Add(($"{userName}:{browser}:{profileDir}", userName, browser, historyDb));
            }
        }
    }

    private static void AddFirefoxProfiles(List<(string, string, string, string)> results, string userName, string profilesDir)
    {
        if (!Directory.Exists(profilesDir)) return;

        foreach (var profileDir in Directory.EnumerateDirectories(profilesDir))
        {
            var placesDb = Path.Combine(profileDir, "places.sqlite");
            if (File.Exists(placesDb))
            {
                results.Add(($"{userName}:Firefox:{profileDir}", userName, "Firefox", placesDb));
            }
        }
    }

    /// <summary>Reads history entries strictly newer than <paramref name="afterUtc"/> from the given database.</summary>
    public List<VisitedSiteEntry> ReadNewEntries(string windowsUser, string browser, string dbPath, DateTime afterUtc)
    {
        var tempCopy = Path.Combine(Path.GetTempPath(), $"pc-history-{Guid.NewGuid():N}.sqlite");
        try
        {
            File.Copy(dbPath, tempCopy, overwrite: true);
            return browser == "Firefox"
                ? ReadFirefox(tempCopy, afterUtc)
                : ReadChromium(tempCopy, afterUtc);
        }
        catch (IOException)
        {
            // Locked or transient copy failure; skip this cycle for this profile.
            return [];
        }
        finally
        {
            try { File.Delete(tempCopy); } catch (IOException) { }
        }

        List<VisitedSiteEntry> ReadChromium(string path, DateTime after)
        {
            var entries = new List<VisitedSiteEntry>();
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT url, title, last_visit_time FROM urls ORDER BY last_visit_time ASC";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var url = reader.GetString(0);
                var title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var webkitTimestamp = reader.GetInt64(2);
                var visitedAtUtc = ChromiumTimestampToUtc(webkitTimestamp);
                if (visitedAtUtc > after)
                {
                    entries.Add(new VisitedSiteEntry(windowsUser, browser, url, title, visitedAtUtc));
                }
            }
            return entries;
        }

        List<VisitedSiteEntry> ReadFirefox(string path, DateTime after)
        {
            var entries = new List<VisitedSiteEntry>();
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT url, title, last_visit_date FROM moz_places WHERE last_visit_date IS NOT NULL ORDER BY last_visit_date ASC";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var url = reader.GetString(0);
                var title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var microsecondsSinceEpoch = reader.GetInt64(2);
                var visitedAtUtc = DateTime.UnixEpoch.AddTicks(microsecondsSinceEpoch * 10);
                if (visitedAtUtc > after)
                {
                    entries.Add(new VisitedSiteEntry(windowsUser, browser, url, title, visitedAtUtc));
                }
            }
            return entries;
        }
    }

    // Chromium timestamps are microseconds since 1601-01-01 (Windows FILETIME epoch).
    private static readonly DateTime ChromiumEpoch = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime ChromiumTimestampToUtc(long webkitTimestamp) =>
        ChromiumEpoch.AddTicks(webkitTimestamp * 10);
}
