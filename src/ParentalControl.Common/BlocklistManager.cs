using System.Text.RegularExpressions;

namespace ParentalControl.Common;

/// <summary>Reads website block rules from a plain-text file (one entry per line, "#" for comments)
/// and enforces them by redirecting matching domains to localhost via the Windows hosts file.</summary>
public sealed partial class BlocklistManager
{
    private const string MarkerBegin = "# === ParentalControl BEGIN ===";
    private const string MarkerEnd = "# === ParentalControl END ===";
    private static readonly string DefaultBlockListTemplatePath = Path.Combine(AppContext.BaseDirectory, "block-list.txt");

    private static string HostsFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public void EnsureBlockListExists()
    {
        PathsConfig.EnsureFoldersExist();

        if (File.Exists(PathsConfig.BlockListFile))
        {
            return;
        }

        if (File.Exists(PathsConfig.LegacyBlocklistFile))
        {
            File.Copy(PathsConfig.LegacyBlocklistFile, PathsConfig.BlockListFile, overwrite: true);
            return;
        }

        if (File.Exists(DefaultBlockListTemplatePath))
        {
            File.Copy(DefaultBlockListTemplatePath, PathsConfig.BlockListFile, overwrite: true);
            return;
        }

        File.WriteAllText(PathsConfig.BlockListFile,
            "# One rule per line: a full domain (example.com) or a partial fragment (casino)\n");
    }

    /// <summary>Loads block rules from the blocklist file. Each line may be a full domain
    /// (example.com) or a partial fragment (gambling) matched against the visited URL.</summary>
    public IReadOnlyList<string> LoadRules()
    {
        EnsureBlockListExists();

        var ruleFiles = new[] { PathsConfig.BlockListFile, PathsConfig.LegacyBlocklistFile }
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return ruleFiles
            .SelectMany(File.ReadAllLines)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Returns true if the given URL matches any block rule (full domain or partial substring match).</summary>
    public bool IsBlocked(string url, IReadOnlyList<string> rules)
    {
        var host = ExtractHost(url);
        foreach (var rule in rules)
        {
            if (host.Equals(rule, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + rule, StringComparison.OrdinalIgnoreCase) ||
                url.Contains(rule, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string ExtractHost(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }
        return url;
    }

    /// <summary>Rewrites the managed section of the hosts file so every full-domain rule
    /// resolves to 127.0.0.1. Requires administrator privileges. Partial/fragment rules
    /// cannot be applied to the hosts file (they need a real domain) and are skipped here.</summary>
    public void ApplyHostsFileBlocks(IReadOnlyList<string> rules)
    {
        var domainRules = rules.Where(r => DomainLikeRegex().IsMatch(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var existingLines = File.Exists(HostsFilePath)
            ? File.ReadAllLines(HostsFilePath).ToList()
            : [];

        var beginIndex = existingLines.IndexOf(MarkerBegin);
        var endIndex = existingLines.IndexOf(MarkerEnd);
        if (beginIndex >= 0 && endIndex > beginIndex)
        {
            existingLines.RemoveRange(beginIndex, endIndex - beginIndex + 1);
        }

        existingLines.Add(MarkerBegin);
        foreach (var domain in domainRules)
        {
            existingLines.Add($"127.0.0.1 {domain}");
            existingLines.Add($"127.0.0.1 www.{domain}");
        }
        existingLines.Add(MarkerEnd);

        File.WriteAllLines(HostsFilePath, existingLines);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-\.]*\.[a-zA-Z]{2,}$")]
    private static partial Regex DomainLikeRegex();
}
