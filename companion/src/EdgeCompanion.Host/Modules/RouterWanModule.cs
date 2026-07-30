using System.Text.Json;

namespace EdgeCompanion.Host.Modules;

public sealed record RouterWanResult(string? Ip, string? Source, string Status);

public sealed class RouterWanModule(IConfiguration configuration, IHttpClientFactory httpClientFactory)
{
    private readonly string? _probeUrl = configuration["EDGE_ROUTER_WAN_URL"];

    public object Capability() => new
    {
        id = "router-wan",
        available = !string.IsNullOrWhiteSpace(_probeUrl),
        status = string.IsNullOrWhiteSpace(_probeUrl) ? "not_configured" : "configured",
    };

    public async Task<RouterWanResult> GetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_probeUrl)) return new(null, null, "not_configured");
        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(3);
        var text = await client.GetStringAsync(_probeUrl, cancellationToken);
        var ip = text.Trim();
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            ip = Read(root, "ip") ?? Read(root, "query") ?? Read(root, "address") ?? "";
        }
        catch (JsonException)
        {
            // Plain-text probes are supported.
        }
        if (string.IsNullOrWhiteSpace(ip)) throw new InvalidOperationException("Router WAN probe returned no address");
        return new(ip, "configured non-VPN probe", "available");
    }

    private static string? Read(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) ? value.GetString() : null;
}
