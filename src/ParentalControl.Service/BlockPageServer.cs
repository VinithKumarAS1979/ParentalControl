using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ParentalControl.Common.WebBlocking;

namespace ParentalControl.Service;

/// <summary>Serves a browser-visible block page on localhost for blocked domains that are
/// redirected through the hosts file.</summary>
public sealed class BlockPageServer(BlockPageCertificateStore certificateStore)
{
    private const int HttpPort = 80;
    private const int HttpsPort = 443;
    private readonly object _rulesLock = new();
    private IReadOnlyList<string> _rules = [];

    public void UpdateRules(IReadOnlyList<string> rules)
    {
        lock (_rulesLock)
        {
            _rules = rules.ToList();
        }
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var httpTask = RunHttpAsync(stoppingToken);
        var httpsTask = RunHttpsAsync(stoppingToken);
        await Task.WhenAll(httpTask, httpsTask);
    }

    private async Task RunHttpAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, HttpPort);
        listener.Start();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await AcceptClientAsync(listener, stoppingToken);
                if (client is not null)
                {
                    _ = HandleHttpClientAsync(client, stoppingToken);
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task RunHttpsAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, HttpsPort);
        listener.Start();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await AcceptClientAsync(listener, stoppingToken);
                if (client is not null)
                {
                    _ = HandleHttpsClientAsync(client, stoppingToken);
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<TcpClient?> AcceptClientAsync(TcpListener listener, CancellationToken stoppingToken)
    {
        try
        {
            return await listener.AcceptTcpClientAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task HandleHttpClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        using var _ = client;
        await using var stream = client.GetStream();

        var request = await ReadHttpRequestAsync(stream, stoppingToken);
        var host = ExtractHost(request);
        var response = BuildBlockPageResponse(host, request.Path, isHttps: false);
        await WriteHttpResponseAsync(stream, response, stoppingToken);
    }

    private async Task HandleHttpsClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        using var _ = client;
        await using var networkStream = client.GetStream();
        var sslOptions = new SslServerAuthenticationOptions
        {
            ServerCertificateSelectionCallback = (_, hostName) => certificateStore.GetCertificateForHost(hostName ?? "blocked.local"),
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        using var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await sslStream.AuthenticateAsServerAsync(sslOptions, stoppingToken);
        var request = await ReadHttpRequestAsync(sslStream, stoppingToken);
        var host = ExtractHost(request);
        var response = BuildBlockPageResponse(host, request.Path, isHttps: true);
        await WriteHttpResponseAsync(sslStream, response, stoppingToken);
    }

    private static async Task WriteHttpResponseAsync(Stream stream, string response, CancellationToken stoppingToken)
    {
        var bytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(bytes, stoppingToken);
        await stream.FlushAsync(stoppingToken);
    }

    private static async Task<HttpRequestInfo> ReadHttpRequestAsync(Stream stream, CancellationToken stoppingToken)
    {
        var buffer = new byte[8192];
        var length = await stream.ReadAsync(buffer, stoppingToken);
        var requestText = Encoding.ASCII.GetString(buffer, 0, length);
        var lines = requestText.Split("\r\n", StringSplitOptions.None);

        var requestLine = lines.FirstOrDefault() ?? string.Empty;
        var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var method = requestParts.Length > 0 ? requestParts[0] : "GET";
        var path = requestParts.Length > 1 ? requestParts[1] : "/";
        var hostHeader = lines.FirstOrDefault(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
        var host = hostHeader is null
            ? "blocked.local"
            : hostHeader.Split(':', 2).ElementAtOrDefault(1)?.Trim() ?? "blocked.local";

        return new HttpRequestInfo(method, path, host);
    }

    private static string ExtractHost(HttpRequestInfo request) => request.Host;

    private string BuildBlockPageResponse(string host, string path, bool isHttps)
    {
                var body = $@"<!doctype html>
<html>
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
    <title>Site blocked</title>
    <style>
        body {{ font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #111827; color: #f9fafb; }}
        .wrap {{ max-width: 760px; margin: 10vh auto; padding: 32px; background: #1f2937; border-radius: 20px; box-shadow: 0 24px 60px rgba(0,0,0,.35); }}
        h1 {{ margin-top: 0; color: #f87171; }}
        code {{ background: #111827; padding: 2px 6px; border-radius: 6px; }}
    </style>
</head>
<body>
    <div class=""wrap"">
        <h1>Website blocked</h1>
        <p>This site is blocked by ParentalControl.</p>
        <p><strong>Host:</strong> <code>{WebUtility.HtmlEncode(host)}</code></p>
        <p><strong>Request:</strong> <code>{WebUtility.HtmlEncode(path)}</code></p>
        <p><strong>Protocol:</strong> {(isHttps ? "HTTPS" : "HTTP")}</p>
    </div>
</body>
</html>";

        return $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
    }

    private sealed record HttpRequestInfo(string Method, string Path, string Host);
}