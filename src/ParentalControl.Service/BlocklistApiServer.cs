using System.Net;
using System.Text;
using System.Text.Json;
using ParentalControl.Common;

namespace ParentalControl.Service;

/// <summary>Serves the current block list on localhost so the browser extension can keep its
/// redirect rules in sync without reading app-data files directly.</summary>
public sealed class BlocklistApiServer(BlocklistManager blocklistManager)
{
    private const int Port = 8787;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        blocklistManager.EnsureBlockListExists();

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        listener.Start();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _ = HandleRequestAsync(context, stoppingToken);
            }
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken stoppingToken)
    {
        try
        {
            if (!string.Equals(context.Request.Url?.AbsolutePath, "/api/blocklist", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await WriteTextAsync(context.Response, "Not found", stoppingToken);
                return;
            }

            var rules = blocklistManager.LoadRules();
            var payload = rules
                .Select((rule, index) => new
                {
                    id = index + 1,
                    rule,
                    kind = IsDomainLike(rule) ? "domain" : "fragment"
                })
                .ToList();

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await WriteTextAsync(context.Response, json, stoppingToken);
        }
        catch (Exception)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteTextAsync(context.Response, "{}", stoppingToken);
        }
        finally
        {
            context.Response.OutputStream.Close();
        }
    }

    private static async Task WriteTextAsync(HttpListenerResponse response, string text, CancellationToken stoppingToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, stoppingToken);
        await response.OutputStream.FlushAsync(stoppingToken);
    }

    private static bool IsDomainLike(string rule) =>
        rule.Contains('.') && !rule.Contains(' ') && !rule.Contains('/') && !rule.Contains('\\');
}