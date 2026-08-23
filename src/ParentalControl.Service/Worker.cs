using ParentalControl.Common;
using ParentalControl.Common.Dns;

namespace ParentalControl.Service;

public class Worker(
    ILogger<Worker> logger,
    BrowserHistoryReader historyReader,
    BlocklistManager blocklistManager,
    VisitLogWriter logWriter,
    DnsProxyServer dnsProxyServer,
    NetworkDnsConfigurator dnsConfigurator) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    private List<NetworkDnsConfigurator.AdapterBackup> _dnsBackups = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PathsConfig.EnsureFoldersExist();
        var state = ScanState.Load();

        try
        {
            _dnsBackups = dnsConfigurator.ApplyLocalDns();
            logger.LogInformation("Redirected {Count} network adapter(s) to the local DNS proxy", _dnsBackups.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to redirect system DNS to the local proxy - service must run with administrator privileges");
        }

        var dnsTask = dnsProxyServer.RunAsync(stoppingToken);
        var historyTask = RunHistoryLoopAsync(state, stoppingToken);

        await Task.WhenAll(dnsTask, historyTask);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_dnsBackups.Count > 0)
        {
            dnsConfigurator.RestoreDns(_dnsBackups);
            logger.LogInformation("Restored original DNS settings for {Count} network adapter(s)", _dnsBackups.Count);
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task RunHistoryLoopAsync(ScanState state, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ScanAndLogVisitedSites(state);
                EnforceBlocklist();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during monitoring cycle");
            }

            try
            {
                await Task.Delay(ScanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ScanAndLogVisitedSites(ScanState state)
    {
        var profiles = historyReader.DiscoverProfiles();
        foreach (var (sourceKey, windowsUser, browser, dbPath) in profiles)
        {
            var lastSeen = state.GetLastSeen(sourceKey);
            var newEntries = historyReader.ReadNewEntries(windowsUser, browser, dbPath, lastSeen);
            if (newEntries.Count == 0) continue;

            logWriter.Append(newEntries);
            state.SetLastSeen(sourceKey, newEntries.Max(e => e.VisitedAtUtc));
            logger.LogInformation("Logged {Count} new visits from {User}'s {Browser}", newEntries.Count, windowsUser, browser);
        }
        state.Save();
    }

    private void EnforceBlocklist()
    {
        var rules = blocklistManager.LoadRules();
        if (rules.Count == 0) return;

        try
        {
            blocklistManager.ApplyHostsFileBlocks(rules);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unable to update hosts file - service must run with administrator privileges");
        }
    }
}


