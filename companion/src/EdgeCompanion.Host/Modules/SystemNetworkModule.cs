using System.Net.NetworkInformation;

namespace EdgeCompanion.Host.Modules;

public sealed record ThroughputSnapshot(
    double DownloadMBps,
    double UploadMBps,
    string? AdapterName,
    bool Available);

public sealed class SystemNetworkModule(ILogger<SystemNetworkModule> logger) : BackgroundService
{
    private readonly object _sync = new();
    private NetworkSample? _previous;
    private ThroughputSnapshot _snapshot = new(0, 0, null, false);

    public ThroughputSnapshot Snapshot
    {
        get { lock (_sync) return _snapshot; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    Sample();
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Unable to sample network throughput");
                }

                if (stoppingToken.IsCancellationRequested) break;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    internal void Sample()
    {
        var active = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .ToArray();

        var now = DateTimeOffset.UtcNow;
        var received = active.Sum(adapter => adapter.GetIPv4Statistics().BytesReceived);
        var sent = active.Sum(adapter => adapter.GetIPv4Statistics().BytesSent);
        var sample = new NetworkSample(received, sent, now);

        lock (_sync)
        {
            if (_previous is not null)
            {
                var seconds = Math.Max(0.001, (now - _previous.At).TotalSeconds);
                _snapshot = new(
                    Math.Max(0, received - _previous.ReceivedBytes) / seconds / 1_000_000,
                    Math.Max(0, sent - _previous.SentBytes) / seconds / 1_000_000,
                    string.Join(", ", active.Select(adapter => adapter.Name)),
                    active.Length > 0);
            }
            _previous = sample;
        }
    }

    private sealed record NetworkSample(long ReceivedBytes, long SentBytes, DateTimeOffset At);
}
