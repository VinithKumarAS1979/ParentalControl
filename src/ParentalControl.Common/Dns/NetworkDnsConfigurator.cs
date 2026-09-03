using System.Management;
using System.Runtime.Versioning;
using System.Text;

namespace ParentalControl.Common.Dns;

/// <summary>Reads the DNS configuration already present on each adapter so the service can
/// log upstream resolvers and forwarders without changing network settings.</summary>
[SupportedOSPlatform("windows")]
public sealed class NetworkDnsConfigurator
{
    public sealed record DnsTopologySnapshot(
        string AdapterDescription,
        bool DhcpEnabled,
        string? DnsDomain,
        string[] DnsServers,
        string ResolverSource);

    public IReadOnlyList<DnsTopologySnapshot> CaptureCurrentTopology()
    {
        var snapshots = new List<DnsTopologySnapshot>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
        using var results = searcher.Get();

        foreach (var managementObject in results)
        {
            using var adapter = (ManagementObject)managementObject;
            var dnsServers = (string[]?)adapter["DNSServerSearchOrder"] ?? [];
            var dhcpEnabled = (bool?)adapter["DHCPEnabled"] ?? false;
            var dnsDomain = adapter["DNSDomain"] as string;
            var resolverSource = dnsServers.Length > 0
                ? "adapter-configured"
                : dhcpEnabled
                    ? "dhcp-or-upstream"
                    : "unspecified";

            snapshots.Add(new DnsTopologySnapshot(
                AdapterDescription: adapter["Description"] as string ?? string.Empty,
                DhcpEnabled: dhcpEnabled,
                DnsDomain: dnsDomain,
                DnsServers: dnsServers,
                ResolverSource: resolverSource));
        }

        return snapshots;
    }

    public void WriteSnapshotLog(DateTime capturedAtUtc, IReadOnlyList<DnsTopologySnapshot> snapshots)
    {
        PathsConfig.EnsureFoldersExist();
        var path = PathsConfig.DnsTopologyLogFileForDate(capturedAtUtc.Date);
        var lines = snapshots.Select(snapshot =>
            $"{capturedAtUtc:yyyy-MM-dd HH:mm:ss} UTC\t{snapshot.AdapterDescription}\t{snapshot.ResolverSource}\t{snapshot.DhcpEnabled}\t{snapshot.DnsDomain ?? string.Empty}\t{FormatServers(snapshot.DnsServers)}");

        File.AppendAllLines(path, lines);
    }

    private static string FormatServers(IEnumerable<string> servers) =>
        string.Join(",", servers.Where(server => !string.IsNullOrWhiteSpace(server)));
}
