using ParentalControl.Common;
using ParentalControl.Common.Dns;

namespace ParentalControl.Service;

public class Worker(
    ILogger<Worker> logger,
    BrowserHistoryReader historyReader,
    VisitLogWriter logWriter,
    NetworkDnsConfigurator dnsConfigurator) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PathsConfig.EnsureFoldersExist();
        var state = ScanState.Load();
        var historyTask = RunHistoryLoopAsync(state, stoppingToken);
        var dnsTopologyTask = RunDnsTopologyLoopAsync(stoppingToken);

        await Task.WhenAll(historyTask, dnsTopologyTask);
    }

    private async Task RunHistoryLoopAsync(ScanState state, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ScanAndLogVisitedSites(state);
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

    private async Task RunDnsTopologyLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshots = dnsConfigurator.CaptureCurrentTopology();
                dnsConfigurator.WriteSnapshotLog(DateTime.UtcNow, snapshots);
                logger.LogInformation("Captured DNS topology for {Count} adapter(s)", snapshots.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while capturing DNS topology");
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
}


