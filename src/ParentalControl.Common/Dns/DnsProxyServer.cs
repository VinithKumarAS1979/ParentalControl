using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using ParentalControl.Common.Models;

namespace ParentalControl.Common.Dns;

/// <summary>A local DNS relay bound to 127.0.0.1:53. Every lookup made by any browser or
/// app on the machine (once adapters are pointed here via <see cref="NetworkDnsConfigurator"/>)
/// passes through this server: it is logged, checked against the blocklist, and either
/// answered with NXDOMAIN (blocked) or forwarded upstream and relayed back (allowed).
/// Note: browsers using DNS-over-HTTPS ("Secure DNS") bypass this server entirely.</summary>
public sealed class DnsProxyServer(
    BlocklistManager blocklistManager,
    VisitLogWriter logWriter,
    ILogger<DnsProxyServer> logger)
{
    private const int DnsPort = 53;
    private static readonly IPEndPoint UpstreamDns = new(IPAddress.Parse("1.1.1.1"), 53);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, DnsPort));
        logger.LogInformation("DNS proxy listening on 127.0.0.1:{Port}", DnsPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await listener.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _ = HandleQueryAsync(listener, received, stoppingToken);
        }
    }

    private async Task HandleQueryAsync(UdpClient listener, UdpReceiveResult received, CancellationToken stoppingToken)
    {
        try
        {
            if (!DnsMessageUtils.TryParseQuestionName(received.Buffer, out var domainName))
            {
                return;
            }

            var rules = blocklistManager.LoadRules();
            var isBlocked = blocklistManager.IsBlocked($"https://{domainName}/", rules);

            // DNS queries carry no OS user/session info, so this source can't be attributed to a specific Windows account.
            logWriter.Append([new VisitedSiteEntry("(all users)", "DNS", domainName, isBlocked ? "[BLOCKED]" : string.Empty, DateTime.UtcNow)]);

            var responseBytes = isBlocked
                ? DnsMessageUtils.BuildNxDomainResponse(received.Buffer)
                : await ForwardToUpstreamAsync(received.Buffer, stoppingToken);

            await listener.SendAsync(responseBytes, received.RemoteEndPoint, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to handle a DNS query");
        }
    }

    private static async Task<byte[]> ForwardToUpstreamAsync(byte[] query, CancellationToken stoppingToken)
    {
        using var upstreamClient = new UdpClient();
        await upstreamClient.SendAsync(query, UpstreamDns, stoppingToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

        var result = await upstreamClient.ReceiveAsync(timeoutCts.Token);
        return result.Buffer;
    }
}
