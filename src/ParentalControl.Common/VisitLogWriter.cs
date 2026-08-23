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
}
