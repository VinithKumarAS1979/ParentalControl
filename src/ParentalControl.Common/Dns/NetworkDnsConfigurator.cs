using System.Management;
using System.Runtime.Versioning;

namespace ParentalControl.Common.Dns;

/// <summary>Points active network adapters at the local DNS proxy (127.0.0.1) so every
/// app's DNS lookups - not just specific browsers - are routed through it, and restores
/// the adapters' original DNS settings afterwards.</summary>
[SupportedOSPlatform("windows")]
public sealed class NetworkDnsConfigurator
{
    public sealed record AdapterBackup(string Description, string[] OriginalDnsServers);

    public List<AdapterBackup> ApplyLocalDns(string localDnsIp = "127.0.0.1")
    {
        var backups = new List<AdapterBackup>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
        using var results = searcher.Get();

        foreach (var managementObject in results)
        {
            using var adapter = (ManagementObject)managementObject;
            var current = (string[]?)adapter["DNSServerSearchOrder"] ?? [];
            backups.Add(new AdapterBackup((string)adapter["Description"], current));

            using var inParams = adapter.GetMethodParameters("SetDNSServerSearchOrder");
            inParams["DNSServerSearchOrder"] = new[] { localDnsIp };
            adapter.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
        }

        return backups;
    }

    public void RestoreDns(IReadOnlyList<AdapterBackup> backups)
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
        using var results = searcher.Get();
        var adapters = results.Cast<ManagementBaseObject>().Cast<ManagementObject>().ToList();

        foreach (var backup in backups)
        {
            var adapter = adapters.FirstOrDefault(a => (string)a["Description"] == backup.Description);
            if (adapter is null) continue;

            using var inParams = adapter.GetMethodParameters("SetDNSServerSearchOrder");
            inParams["DNSServerSearchOrder"] = backup.OriginalDnsServers.Length > 0 ? backup.OriginalDnsServers : null;
            adapter.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
        }

        foreach (var adapter in adapters)
        {
            adapter.Dispose();
        }
    }
}
