using ParentalControl.Common.Models;

namespace ParentalControl.Common;

/// <summary>Appends visited-site entries to the daily log file at C:\temp\log-yyyy-MM-dd.txt.</summary>
public sealed class VisitLogWriter
{
    private static readonly object WriteLock = new();

    public void Append(IEnumerable<VisitedSiteEntry> entries)
    {
        var byDate = entries.GroupBy(e => e.VisitedAtUtc.Date);
        lock (WriteLock)
        {
            PathsConfig.EnsureFoldersExist();
            foreach (var group in byDate)
            {
                var path = PathsConfig.LogFileForDate(group.Key);
                var lines = group.Select(e => e.ToLogLine());
                File.AppendAllLines(path, lines);
            }
        }
    }

    /// <summary>Logs terminated (blocked) application launches alongside visited-site entries,
    /// so they show up in the same daily log and viewer.</summary>
    public void AppendAppBlockEvents(IEnumerable<AppBlockEvent> events)
    {
        var nowUtc = DateTime.UtcNow;
        var entries = events.Select(e => new VisitedSiteEntry(e.WindowsUser, "AppBlock", e.Path, "Blocked - terminated", nowUtc));
        Append(entries);
    }
}
