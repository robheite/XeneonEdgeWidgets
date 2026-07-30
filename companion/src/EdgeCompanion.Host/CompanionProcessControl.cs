using System.Net;

namespace EdgeCompanion.Host;

public static class CompanionProcessControl
{
    public const string ShutdownEventName = @"Local\XeneonEdgeWidgets.EdgeCompanion.Shutdown";
    private static readonly Uri HealthUri = new("http://127.0.0.1:48620/api/v1/health");

    public static bool IsStartCommand(string[] arguments) =>
        arguments.Length == 1 && arguments[0].Equals("--start", StringComparison.OrdinalIgnoreCase);

    public static bool IsStopCommand(string[] arguments) =>
        arguments.Length == 1 && arguments[0].Equals("--stop", StringComparison.OrdinalIgnoreCase);

    public static async Task<bool> HandleLaunchCommandAsync(string[] arguments)
    {
        if (IsStartCommand(arguments))
            return await IsHealthyAsync();

        if (!IsStopCommand(arguments))
            return false;

        if (!OperatingSystem.IsWindows())
            return true;

        try
        {
            using var shutdownEvent = EventWaitHandle.OpenExisting(ShutdownEventName);
            shutdownEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return true;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!await IsHealthyAsync()) return true;
            await Task.Delay(150);
        }
        return true;
    }

    public static async Task<bool> IsHealthyAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(700) };
        try
        {
            using var response = await client.GetAsync(HealthUri);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
