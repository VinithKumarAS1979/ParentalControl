using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace ParentalControl.Common;

/// <summary>Reads application block rules from a plain-text file (one entry per line, "#" for
/// comments) and enforces them by terminating any running process that matches a rule.</summary>
[SupportedOSPlatform("windows")]
public sealed class AppBlockManager
{
    /// <summary>Loads block rules from the app-blocklist file. Each line may be an executable
    /// name (notepad.exe) or a path fragment (Steam\steam.exe) matched against the running process.</summary>
    public IReadOnlyList<string> LoadRules()
    {
        if (!File.Exists(PathsConfig.AppBlocklistFile))
        {
            PathsConfig.EnsureFoldersExist();
            File.WriteAllText(PathsConfig.AppBlocklistFile,
                "# One rule per line: an executable name (notepad.exe) or a path fragment (Steam\\steam.exe)\n");
            return [];
        }

        return File.ReadAllLines(PathsConfig.AppBlocklistFile)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Terminates every running process that matches a block rule (by executable name
    /// or by a substring of its full path). Returns a description of each process killed, for logging.</summary>
    public List<AppBlockEvent> EnforceBlocklist(IReadOnlyList<string> rules)
    {
        var killed = new List<AppBlockEvent>();
        if (rules.Count == 0) return killed;

        var currentProcessId = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == currentProcessId) continue;

                string? path = null;
                try { path = process.MainModule?.FileName; } catch { /* protected/system process */ }

                var exeName = process.ProcessName + ".exe";
                if (!IsBlocked(exeName, path, rules)) continue;

                var owner = TryGetOwner(process.Id);
                try
                {
                    process.Kill(entireProcessTree: true);
                    killed.Add(new AppBlockEvent(owner, exeName, path ?? exeName));
                }
                catch { /* access denied - e.g. a protected system process, nothing more we can do */ }
            }
        }

        return killed;
    }

    private static bool IsBlocked(string exeName, string? path, IReadOnlyList<string> rules)
    {
        foreach (var rule in rules)
        {
            if (exeName.Equals(rule, StringComparison.OrdinalIgnoreCase)) return true;
            if (path is not null && path.Contains(rule, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string TryGetOwner(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Process WHERE ProcessId = {processId}");
            using var results = searcher.Get();
            foreach (var managementObject in results)
            {
                using var mo = (ManagementObject)managementObject;
                var args = new object[2];
                if ((uint)mo.InvokeMethod("GetOwner", args) == 0)
                {
                    return (string)args[0];
                }
            }
        }
        catch { /* ignore - owner is best-effort, used only for logging */ }

        return "(unknown)";
    }
}

/// <summary>A single blocked-application termination, for logging.</summary>
public sealed record AppBlockEvent(string WindowsUser, string ProcessName, string Path);
