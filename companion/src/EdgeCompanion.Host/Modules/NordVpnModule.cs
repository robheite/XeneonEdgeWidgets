using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EdgeCompanion.Host.Modules;

public sealed record NordVpnStatus(
    string State,
    string? Server,
    string? Protocol,
    string? City,
    string? Country,
    DateTimeOffset? PausedUntil,
    string? Source);

public sealed record ActionResult(string Message, DateTimeOffset? PausedUntil = null);
public sealed record NordVpnConnectionMetadata(string Server, string HostName, string? City, string? Country);

public sealed class NordVpnModule(ILogger<NordVpnModule> logger)
{
    private const string DefaultExecutable = @"C:\Program Files\NordVPN\NordVPN.exe";
    private static readonly string NordVpnDataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NordVPN");
    private readonly SemaphoreSlim _actionLock = new(1, 1);
    private readonly string? _executable = File.Exists(DefaultExecutable) ? DefaultExecutable : null;
    private readonly NordVpnConnectionReader _connectionReader = new(NordVpnDataDirectory);
    private CancellationTokenSource? _resumeCancellation;
    private DateTimeOffset? _pausedUntil;

    public object Capability() => new
    {
        id = "nordvpn",
        available = _executable is not null,
        status = _executable is null ? "not_installed" : "available",
        actions = _executable is null ? Array.Empty<string>() : new[] { "connect-fastest-us", "pause" },
    };

    public Task<NordVpnStatus> GetStatusAsync()
    {
        if (_pausedUntil > DateTimeOffset.UtcNow)
            return Task.FromResult(new NordVpnStatus("paused", "Paused", null, null, null, _pausedUntil, "edge-companion"));

        var adapter = FindAdapter();
        var connected = adapter?.OperationalStatus == OperationalStatus.Up;
        var connection = connected ? _connectionReader.ReadCurrent() : null;
        return Task.FromResult(new NordVpnStatus(
            connected ? "connected" : "disconnected",
            connection?.Server,
            connected ? adapter?.Description : null,
            connection?.City,
            connection?.Country,
            null,
            connection is null ? "windows-network-adapter" : "nordvpn-local-state"));
    }

    public async Task<ActionResult> ConnectFastestUsAsync()
    {
        await EnterActionAsync();
        try
        {
            ClearPause();
            await RunAsync(["-c", "-g", "United States"], TimeSpan.FromSeconds(30));
            return new("Connecting to the fastest United States server");
        }
        finally
        {
            _actionLock.Release();
        }
    }

    public async Task<ActionResult> PauseAsync(int minutes)
    {
        if (minutes is not (5 or 15 or 30 or 60))
            throw new ModuleException("invalid_pause", "Pause duration must be 5, 15, 30, or 60 minutes", 400);

        await EnterActionAsync();
        try
        {
            await RunAsync(["-d"], TimeSpan.FromSeconds(15));
            _pausedUntil = DateTimeOffset.UtcNow.AddMinutes(minutes);
            _resumeCancellation?.Cancel();
            _resumeCancellation?.Dispose();
            _resumeCancellation = new CancellationTokenSource();
            _ = ResumeAfterPauseAsync(_pausedUntil.Value, _resumeCancellation.Token);
            return new($"VPN paused for {minutes} minutes", _pausedUntil);
        }
        finally
        {
            _actionLock.Release();
        }
    }

    private async Task ResumeAfterPauseAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(deadline - DateTimeOffset.UtcNow, cancellationToken);
            await ConnectFastestUsAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to resume NordVPN after pause");
        }
    }

    private async Task EnterActionAsync()
    {
        if (_executable is null) throw new ModuleException("not_installed", "NordVPN is not installed");
        if (!await _actionLock.WaitAsync(0)) throw new ModuleException("action_busy", "A NordVPN action is already in progress", 409);
    }

    private async Task RunAsync(IEnumerable<string> arguments, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new()
            {
                FileName = _executable!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new ModuleException("timeout", "NordVPN did not complete the action in time", 504);
        }
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new ModuleException("action_failed", string.IsNullOrWhiteSpace(error) ? "NordVPN action failed" : error.Trim());
        }
    }

    private static NetworkInterface? FindAdapter() =>
        NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(adapter =>
            adapter.Name.Contains("Nord", StringComparison.OrdinalIgnoreCase)
            || adapter.Description.Contains("NordLynx", StringComparison.OrdinalIgnoreCase)
            || adapter.Description.Contains("NordVPN", StringComparison.OrdinalIgnoreCase)
            || adapter.Description.Contains("TAP-NordVPN", StringComparison.OrdinalIgnoreCase));

    private void ClearPause()
    {
        _pausedUntil = null;
        _resumeCancellation?.Cancel();
        _resumeCancellation?.Dispose();
        _resumeCancellation = null;
    }
}

public sealed class NordVpnConnectionReader(string dataDirectory)
{
    private static readonly Regex StatusPattern = new(
        @"VpnConnectionServiceStatus changed - (?<state>Connected|Disconnected)\. Server: (?<name>.+?) \((?<host>[^)]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private string? _lastLogPath;
    private DateTime _lastLogWriteUtc;
    private NordVpnConnectionMetadata? _cached;

    public NordVpnConnectionMetadata? ReadCurrent()
    {
        var logsDirectory = Path.Combine(dataDirectory, "logs");
        var log = Directory.Exists(logsDirectory)
            ? new DirectoryInfo(logsDirectory)
                .EnumerateFiles("app-*.log")
                .Where(file => Regex.IsMatch(file.Name, @"^app-\d{8}\.log$", RegexOptions.CultureInvariant))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (log is null) return null;

        if (log.FullName == _lastLogPath && log.LastWriteTimeUtc == _lastLogWriteUtc)
            return _cached;

        _lastLogPath = log.FullName;
        _lastLogWriteUtc = log.LastWriteTimeUtc;
        _cached = ResolveLatest(ReadSharedLines(log.FullName), Path.Combine(dataDirectory, "servers_v2.json"));
        return _cached;
    }

    private static IEnumerable<string> ReadSharedLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            yield return line;
    }

    public static NordVpnConnectionMetadata? ResolveLatest(IEnumerable<string> lines, string catalogPath)
    {
        Match? latest = null;
        foreach (var line in lines)
        {
            var match = StatusPattern.Match(line);
            if (match.Success) latest = match;
        }

        if (latest is null || latest.Groups["state"].Value == "Disconnected")
            return null;

        var name = latest.Groups["name"].Value;
        var host = latest.Groups["host"].Value;
        var serverLabel = name[name.LastIndexOf('#')..];
        var location = ResolveLocation(catalogPath, host);
        return new NordVpnConnectionMetadata(serverLabel, host, location?.City, location?.Country);
    }

    private static NordVpnLocation? ResolveLocation(string catalogPath, string host)
    {
        if (!File.Exists(catalogPath)) return null;

        using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var root = document.RootElement;
        var server = root.GetProperty("Servers").EnumerateArray()
            .FirstOrDefault(item =>
                item.TryGetProperty("HostName", out var hostName)
                && hostName.GetString()?.Equals(host, StringComparison.OrdinalIgnoreCase) == true);
        if (server.ValueKind == JsonValueKind.Undefined
            || !server.TryGetProperty("location_ids", out var locationIds)
            || locationIds.GetArrayLength() == 0)
            return null;

        var locationId = locationIds[0].GetInt32();
        var location = root.GetProperty("Locations").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("Id").GetInt32() == locationId);
        if (location.ValueKind == JsonValueKind.Undefined
            || !location.TryGetProperty("Country", out var country))
            return null;

        var city = country.TryGetProperty("city", out var cityObject)
            && cityObject.TryGetProperty("name", out var cityName)
                ? cityName.GetString()
                : null;
        var countryName = country.TryGetProperty("Name", out var nameProperty)
            ? nameProperty.GetString()
            : null;
        return new NordVpnLocation(city, countryName);
    }

    private sealed record NordVpnLocation(string? City, string? Country);
}
