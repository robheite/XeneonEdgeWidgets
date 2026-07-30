using EdgeCompanion.Host;
using EdgeCompanion.Host.Modules;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["EDGE_COMPANION_URL"] ?? "http://127.0.0.1:48620");
builder.Services.AddHttpClient();
builder.Services.AddSingleton<SystemNetworkModule>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SystemNetworkModule>());
builder.Services.AddSingleton<PublicIpModule>();
builder.Services.AddSingleton<RouterWanModule>();
builder.Services.AddSingleton<NordVpnModule>();
builder.Services.AddSingleton<StartupModule>();

var app = builder.Build();
app.Use(async (context, next) =>
{
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
        context.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
        context.Response.Headers.AccessControlAllowHeaders = "Content-Type, X-Edge-Token";
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

api.MapGet("/capabilities", (NordVpnModule nordVpn, RouterWanModule routerWan, StartupModule startup) =>
    ApiEnvelope.From(new object[]
    {
        nordVpn.Capability(),
        routerWan.Capability(),
        new { id = "windows-startup", available = startup.Get().Supported },
        new { id = "system-network", available = true },
        new { id = "public-ip", available = true },
    }));

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
    IConfiguration configuration) =>
{
    if (!ActionAuthorization.IsAllowed(context.Request, configuration["EDGE_COMPANION_TOKEN"]))
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
    IConfiguration configuration) =>
{
    if (!ActionAuthorization.IsAllowed(context.Request, configuration["EDGE_COMPANION_TOKEN"]))
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
    IConfiguration configuration) =>
{
    if (!ActionAuthorization.IsAllowed(context.Request, configuration["EDGE_COMPANION_TOKEN"]))
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

app.Run();

public partial class Program;
