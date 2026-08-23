using System.Text.Json;

namespace ParentalControl.Common;

/// <summary>Tracks the last-processed visit timestamp per browser profile so the same
/// history entry is never logged twice across service restarts.</summary>
public sealed class ScanState
{
    private readonly Dictionary<string, long> _lastSeenUtcTicks;

    private ScanState(Dictionary<string, long> lastSeenUtcTicks)
    {
        _lastSeenUtcTicks = lastSeenUtcTicks;
    }

    public static ScanState Load()
    {
        try
        {
            if (File.Exists(PathsConfig.StateFile))
            {
                var json = File.ReadAllText(PathsConfig.StateFile);
                var data = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
                if (data is not null)
                {
                    return new ScanState(data);
                }
            }
        }
        catch (IOException) { }
        catch (JsonException) { }

        return new ScanState(new Dictionary<string, long>());
    }

    public DateTime GetLastSeen(string sourceKey) =>
        _lastSeenUtcTicks.TryGetValue(sourceKey, out var ticks) ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;

    public void SetLastSeen(string sourceKey, DateTime utc) =>
        _lastSeenUtcTicks[sourceKey] = utc.Ticks;

    public void Save()
    {
        PathsConfig.EnsureFoldersExist();
        var json = JsonSerializer.Serialize(_lastSeenUtcTicks);
        File.WriteAllText(PathsConfig.StateFile, json);
    }
}
