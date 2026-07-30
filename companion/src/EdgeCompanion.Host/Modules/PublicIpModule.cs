using System.Text.Json;

namespace EdgeCompanion.Host.Modules;

public sealed record PublicIpResult(string Ip, string Source);

public sealed class PublicIpModule(IHttpClientFactory httpClientFactory)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private PublicIpResult? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<PublicIpResult> GetAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromSeconds(30))
            return _cached;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromSeconds(30))
                return _cached;

            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            try
            {
                var json = await client.GetStringAsync("https://api.ipify.org?format=json", cancellationToken);
                var ip = JsonDocument.Parse(json).RootElement.GetProperty("ip").GetString();
                if (!string.IsNullOrWhiteSpace(ip)) return Cache(new(ip, "ipify"));
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Try the fallback provider.
            }

            var trace = await client.GetStringAsync("https://1.1.1.1/cdn-cgi/trace", cancellationToken);
            var fallbackIp = trace.Split('\n')
                .FirstOrDefault(line => line.StartsWith("ip=", StringComparison.Ordinal))
                ?.Split('=', 2)[1].Trim();
            if (string.IsNullOrWhiteSpace(fallbackIp)) throw new InvalidOperationException("Public IP providers returned no address");
            return Cache(new(fallbackIp, "Cloudflare"));
        }
        finally
        {
            _lock.Release();
        }
    }

    private PublicIpResult Cache(PublicIpResult result)
    {
        _cached = result;
        _cachedAt = DateTimeOffset.UtcNow;
        return result;
    }
}
