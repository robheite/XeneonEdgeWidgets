using EdgeCompanion.Host;
using EdgeCompanion.Host.Modules;

if (await CompanionProcessControl.HandleLaunchCommandAsync(args))
    return;

var effectiveArgs = CompanionProcessControl.IsStartCommand(args) ? Array.Empty<string>() : args;
var builder = WebApplication.CreateBuilder(effectiveArgs);
builder.WebHost.UseUrls(builder.Configuration["EDGE_COMPANION_URL"] ?? "http://127.0.0.1:48620");
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("emby", client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<SystemNetworkModule>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SystemNetworkModule>());
builder.Services.AddSingleton<PublicIpModule>();
builder.Services.AddSingleton<RouterWanModule>();
builder.Services.AddSingleton<NordVpnModule>();
builder.Services.AddSingleton<WandRemoteModule>();
builder.Services.AddSingleton<EmbyModule>();
builder.Services.AddSingleton<StartupModule>();
builder.Services.AddSingleton<ActionTokenProvider>();

var app = builder.Build();
app.Use(async (context, next) =>
{
    // Wand serves third-party content from localhost while trusted widgets use
    // 127.0.0.1. They share one listener but remain distinct browser origins.
    if (context.Request.Path.StartsWithSegments("/api")
        && context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    var origin = context.Request.Headers.Origin.ToString();
    if (!HttpMethods.IsGet(context.Request.Method))
    {
        app.Logger.LogInformation(
            "Widget action request {Method} {Path} Origin={Origin}",
            context.Request.Method,
            context.Request.Path,
            string.IsNullOrEmpty(origin) ? "(none)" : origin);
    }
    if (OriginPolicy.IsAllowed(origin))
    {
        context.Response.Headers.AccessControlAllowOrigin = origin;
        context.Response.Headers.Vary = "Origin";
    }

    if (HttpMethods.IsOptions(context.Request.Method))
    {
        context.Response.Headers.AccessControlAllowMethods = "GET, POST, DELETE, OPTIONS";
        context.Response.Headers.AccessControlAllowHeaders = "Content-Type, X-Edge-Token, X-Emby-Token";
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }

    await next();
});

var api = app.MapGroup("/api/v1");

api.MapGet("/health", () => ApiEnvelope.From(new
{
    name = "Edge Companion",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0",
    uptimeSeconds = (long)(DateTimeOffset.UtcNow - ProcessInfo.StartedAt).TotalSeconds,
}));

api.MapGet("/capabilities", (NordVpnModule nordVpn, RouterWanModule routerWan, StartupModule startup, WandRemoteModule wandRemote, EmbyModule emby) =>
    ApiEnvelope.From(new object[]
    {
        nordVpn.Capability(),
        wandRemote.Capability(),
        emby.Capability(),
        routerWan.Capability(),
        new { id = "windows-startup", available = startup.Get().Supported },
        new { id = "system-network", available = true },
        new { id = "public-ip", available = true },
    }));

api.MapGet("/emby/public-users", async (string serverUrl, EmbyModule emby, CancellationToken ct) =>
    await ProxyEmby(emby, serverUrl, HttpMethod.Get, "Users/Public", null, null, null, ct));

api.MapPost("/emby/authenticate", async (EmbyAuthRequest auth, EmbyModule emby, CancellationToken ct) =>
    await ProxyEmby(emby, auth.ServerUrl, HttpMethod.Post, "Users/AuthenticateByName", null, null,
        new { Username = auth.Username, Pw = auth.Password }, ct));

api.MapGet("/emby/users/{userId}/views", async (HttpRequest request, string userId, string serverUrl, EmbyModule emby, CancellationToken ct) =>
    await ProxyEmby(emby, serverUrl, HttpMethod.Get, $"Users/{Uri.EscapeDataString(userId)}/Views", request.Headers["X-Emby-Token"], request, null, ct));

api.MapGet("/emby/users/{userId}/items", async (HttpRequest request, string userId, string serverUrl, string? parentId, string? searchTerm, EmbyModule emby, CancellationToken ct) =>
{
    var query = new Dictionary<string, string?> { ["ParentId"] = parentId, ["SearchTerm"] = searchTerm, ["Recursive"] = string.IsNullOrWhiteSpace(searchTerm) ? "false" : "true", ["Limit"] = "60", ["SortBy"] = "SortName", ["SortOrder"] = "Ascending", ["Fields"] = "Overview,PrimaryImageAspectRatio,MediaSources,RunTimeTicks" };
    var suffix = string.Join("&", query.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value!)}"));
    return await ProxyEmby(emby, serverUrl, HttpMethod.Get, $"Users/{Uri.EscapeDataString(userId)}/Items?{suffix}", request.Headers["X-Emby-Token"], request, null, ct);
});

api.MapGet("/emby/users/{userId}/items/{itemId}", async (HttpRequest request, string userId, string itemId, string serverUrl, EmbyModule emby, CancellationToken ct) =>
    await ProxyEmby(emby, serverUrl, HttpMethod.Get, $"Users/{Uri.EscapeDataString(userId)}/Items/{Uri.EscapeDataString(itemId)}", request.Headers["X-Emby-Token"], request, null, ct));

api.MapPost("/emby/items/{itemId}/playback-info", async (HttpRequest request, string itemId, string serverUrl, string userId, EmbyModule emby, CancellationToken ct) =>
    await ProxyEmby(emby, serverUrl, HttpMethod.Post, $"Items/{Uri.EscapeDataString(itemId)}/PlaybackInfo?UserId={Uri.EscapeDataString(userId)}", request.Headers["X-Emby-Token"], request,
        new
        {
            UserId = userId,
            AutoOpenLiveStream = true,
            IsPlayback = true,
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true,
            DeviceProfile = new
            {
                Name = "XENEON EDGE",
                MaxStreamingBitrate = 12_000_000,
                DirectPlayProfiles = new[] { new { Container = "webm", Type = "Video", VideoCodec = "vpx,vp8,vp9", AudioCodec = "vorbis" } },
                TranscodingProfiles = new[] { new { Container = "webm", Type = "Video", VideoCodec = "vpx", AudioCodec = "vorbis", Protocol = "http", Context = "Streaming", MaxAudioChannels = "2", CopyTimestamps = false, EstimateContentLength = false, TranscodeSeekInfo = "Auto" } },
                ResponseProfiles = new[] { new { Container = "webm", Type = "Video", VideoCodec = "vpx", AudioCodec = "vorbis", MimeType = "video/webm" } },
            },
        }, ct));

api.MapGet("/emby/items/{itemId}/image", async (HttpRequest request, string itemId, string serverUrl, string accessToken, int? width, EmbyModule emby, CancellationToken ct) =>
    await ProxyEmby(emby, serverUrl, HttpMethod.Get, $"Items/{Uri.EscapeDataString(itemId)}/Images/Primary?maxWidth={width ?? 480}&quality=88", accessToken, request, null, ct, false));

api.MapGet("/emby/videos/{itemId}/stream.webm", async (HttpRequest request, string itemId, string serverUrl, string accessToken, string mediaSourceId, string playSessionId, int? audioStreamIndex, int? subtitleStreamIndex, long? startTimeTicks, EmbyModule emby, CancellationToken ct) =>
{
    var path = EmbyModule.BuildVideoStreamPath(itemId, mediaSourceId, playSessionId, audioStreamIndex, subtitleStreamIndex, startTimeTicks);
    return await ProxyEmby(emby, serverUrl, HttpMethod.Get, path, accessToken, request, null, ct, false, "video/webm");
});

api.MapPost("/emby/playback/{eventName}", async (string eventName, EmbyPlaybackRequest report, EmbyModule emby, CancellationToken ct) =>
{
    var path = eventName.ToLowerInvariant() switch { "started" => "Sessions/Playing", "progress" => "Sessions/Playing/Progress", "stopped" => "Sessions/Playing/Stopped", _ => throw new ModuleException("invalid_playback_event", "Unknown playback event", 400) };
    return await ProxyEmby(emby, report.ServerUrl, HttpMethod.Post, path, report.AccessToken, null, report.Playback, ct);
});

api.MapPost("/emby/users/{userId}/items/{itemId}/watched", async (string userId, string itemId, EmbyWatchedRequest watched, EmbyModule emby, CancellationToken ct) =>
    await ProxyEmby(emby, watched.ServerUrl, watched.Played ? HttpMethod.Post : HttpMethod.Delete, $"Users/{Uri.EscapeDataString(userId)}/PlayedItems/{Uri.EscapeDataString(itemId)}", watched.AccessToken, null, null, ct));

app.MapGet("/wand/bridge.js", () => Results.Content(WandRemoteModule.BridgeScript, "application/javascript"));
app.MapGet("/wand/remote/{**path}", async (HttpContext context, string? path, WandRemoteModule module, CancellationToken cancellationToken) =>
{
    try { await module.ProxyAsync(context, path, cancellationToken); }
    catch (HttpRequestException) when (!context.Response.HasStarted)
    { await Results.Json(ApiEnvelope.Error("remote_unavailable", "Wand Remote is unavailable"), statusCode: 502).ExecuteAsync(context); }
});
app.MapMethods("/wand/upstream/{host}/{**path}", new[] { "GET", "POST" }, async (HttpContext context, string host, string? path, WandRemoteModule module, CancellationToken cancellationToken) =>
{
    try { await module.ProxyUpstreamAsync(context, host, path, cancellationToken); }
    catch (ModuleException) when (!context.Response.HasStarted) { context.Response.StatusCode = StatusCodes.Status404NotFound; }
    catch (HttpRequestException) when (!context.Response.HasStarted)
    { await Results.Json(ApiEnvelope.Error("remote_unavailable", "Wand catalog is unavailable"), statusCode: 502).ExecuteAsync(context); }
});

api.MapGet("/auth/token", (HttpContext context, ActionTokenProvider tokenProvider) =>
{
    if (!OriginPolicy.IsAllowed(context.Request.Headers.Origin.ToString()))
        return Results.Json(ApiEnvelope.Error("origin_not_allowed", "Token bootstrap requires an iCUE or localhost origin"), statusCode: 403);
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(ApiEnvelope.From(new { token = tokenProvider.Token }));
});

api.MapGet("/nordvpn/dashboard", async (
    NordVpnModule nordVpn,
    PublicIpModule publicIp,
    RouterWanModule routerWan,
    SystemNetworkModule network,
    CancellationToken cancellationToken) =>
{
    var vpnTask = SafeResult<NordVpnStatus>.Capture("nordvpn", nordVpn.GetStatusAsync);
    var publicIpTask = SafeResult<PublicIpResult>.Capture("public-ip", () => publicIp.GetAsync(cancellationToken));
    var routerTask = SafeResult<RouterWanResult>.Capture("router-wan", () => routerWan.GetAsync(cancellationToken));
    await Task.WhenAll(vpnTask, publicIpTask, routerTask);

    var vpn = await vpnTask;
    var machineIp = await publicIpTask;
    var router = await routerTask;
    return ApiEnvelope.From(new
    {
        vpn = vpn.Value ?? new NordVpnStatus("unknown", null, null, null, null, null, null),
        network = new
        {
            machinePublicIp = machineIp.Value?.Ip,
            machineLocation = machineIp.Value is null ? machineIp.Error?.Message : $"Observed by {machineIp.Value.Source}",
            routerWanIp = router.Value?.Ip,
            routerSource = router.Value?.Source,
            routerStatus = router.Value?.Status ?? "unavailable",
        },
        throughput = network.Snapshot,
    }, new[] { vpn.Error, machineIp.Error, router.Error }.Where(error => error is not null));
});

api.MapGet("/system/startup", (StartupModule startup) =>
    ApiEnvelope.From(startup.Get()));

api.MapPost("/system/startup", (
    HttpContext context,
    StartupRequest request,
    StartupModule startup,
    ActionTokenProvider tokenProvider) =>
{
    if (!ActionAuthorization.IsAllowed(context.Request, tokenProvider.Token))
        return Results.Json(ApiEnvelope.Error("unauthorized", "Missing or invalid action token"), statusCode: 401);
    try
    {
        return Results.Ok(ApiEnvelope.From(startup.Set(request.Enabled)));
    }
    catch (ModuleException exception)
    {
        return Results.Json(ApiEnvelope.Error(exception.Code, exception.Message), statusCode: exception.StatusCode);
    }
});

api.MapPost("/nordvpn/actions/pause", async (
    HttpContext context,
    PauseRequest request,
    NordVpnModule nordVpn,
    ActionTokenProvider tokenProvider) =>
{
    if (!ActionAuthorization.IsAllowed(context.Request, tokenProvider.Token))
        return Results.Json(ApiEnvelope.Error("unauthorized", "Missing or invalid action token"), statusCode: 401);

    try
    {
        return Results.Ok(ApiEnvelope.From(await nordVpn.PauseAsync(request.Minutes)));
    }
    catch (ModuleException exception)
    {
        return Results.Json(ApiEnvelope.Error(exception.Code, exception.Message), statusCode: exception.StatusCode);
    }
});

api.MapPost("/nordvpn/actions/connect-fastest-us", async (
    HttpContext context,
    NordVpnModule nordVpn,
    ActionTokenProvider tokenProvider) =>
{
    if (!ActionAuthorization.IsAllowed(context.Request, tokenProvider.Token))
        return Results.Json(ApiEnvelope.Error("unauthorized", "Missing or invalid action token"), statusCode: 401);

    try
    {
        return Results.Ok(ApiEnvelope.From(await nordVpn.ConnectFastestUsAsync()));
    }
    catch (ModuleException exception)
    {
        return Results.Json(ApiEnvelope.Error(exception.Code, exception.Message), statusCode: exception.StatusCode);
    }
});

using var shutdownEvent = new EventWaitHandle(
    initialState: false,
    EventResetMode.AutoReset,
    CompanionProcessControl.ShutdownEventName);
var shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
    shutdownEvent,
    (_, _) => app.Lifetime.StopApplication(),
    state: null,
    millisecondsTimeOutInterval: Timeout.Infinite,
    executeOnlyOnce: true);

try { app.Run(); }
finally { shutdownRegistration.Unregister(null); }

static async Task<IResult> ProxyEmby(EmbyModule module, string serverUrl, HttpMethod method, string path, string? token, HttpRequest? request, object? body, CancellationToken ct, bool json = true, string? fallbackContentType = null)
{
    try
    {
        var response = await module.SendAsync(serverUrl, method, path, token, request, body, ct);
        return new ProxyResponseResult(response, fallbackContentType ?? (json ? "application/json" : "application/octet-stream"));
    }
    catch (ModuleException exception) { return Results.Json(ApiEnvelope.Error(exception.Code, exception.Message), statusCode: exception.StatusCode); }
    catch (HttpRequestException exception) { return Results.Json(ApiEnvelope.Error("emby_unavailable", exception.Message), statusCode: 502); }
}

public partial class Program;
