using EdgeCompanion.Host;
using EdgeCompanion.Host.Modules;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EdgeCompanion.Host.Tests;

public class ApiTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("file://")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://localhost:48620")]
    public void Local_widget_origins_are_allowed(string origin)
    {
        Assert.True(OriginPolicy.IsAllowed(origin));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("not a URI")]
    [InlineData("")]
    public void Non_local_origins_are_rejected(string origin)
    {
        Assert.False(OriginPolicy.IsAllowed(origin));
    }

    [Fact]
    public async Task Safe_result_preserves_partial_failures()
    {
        var result = await SafeResult<string>.Capture("test", () => throw new InvalidOperationException("offline"));
        Assert.Null(result.Value);
        Assert.Equal("test", result.Error?.Module);
        Assert.Equal("offline", result.Error?.Message);
    }

    [Theory]
    [InlineData("--start", true)]
    [InlineData("--START", true)]
    [InlineData("--stop", false)]
    [InlineData("", false)]
    public void Companion_recognizes_only_the_exact_start_command(string argument, bool expected)
    {
        Assert.Equal(expected, CompanionProcessControl.IsStartCommand([argument]));
    }

    [Theory]
    [InlineData("--stop", true)]
    [InlineData("--STOP", true)]
    [InlineData("--start", false)]
    [InlineData("", false)]
    public void Companion_recognizes_only_the_exact_stop_command(string argument, bool expected)
    {
        Assert.Equal(expected, CompanionProcessControl.IsStopCommand([argument]));
    }

    [Fact]
    public void Nordvpn_connection_reader_uses_latest_status_and_catalog_location()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"edge-companion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var catalogPath = Path.Combine(directory, "servers_v2.json");
        File.WriteAllText(catalogPath, """
            {
              "Servers": [
                { "Name": "United States #9536", "HostName": "us9536.nordvpn.com", "location_ids": [4053] }
              ],
              "Locations": [
                { "Id": 4053, "Country": { "Name": "United States", "city": { "name": "Ashburn" } } }
              ]
            }
            """);

        try
        {
            var result = NordVpnConnectionReader.ResolveLatest(
                [
                    "VpnConnectionServiceStatus changed - Connected. Server: United States #9527 (us9527.nordvpn.com)",
                    "VpnConnectionServiceStatus changed - Connected. Server: United States #9536 (us9536.nordvpn.com)",
                ],
                catalogPath);

            Assert.Equal("#9536", result?.Server);
            Assert.Equal("Ashburn", result?.City);
            Assert.Equal("United States", result?.Country);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Action_token_is_generated_once_and_reused()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"edge-token-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "action-token");
        try
        {
            var first = new ActionTokenProvider(null, path);
            var second = new ActionTokenProvider(null, path);

            Assert.Equal(64, first.Token.Length);
            Assert.Equal(first.Token, second.Token);
            Assert.Equal(first.Token, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Configured_action_token_overrides_generated_storage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"unused-token-{Guid.NewGuid():N}");
        var provider = new ActionTokenProvider("configured-token", path);

        Assert.Equal("configured-token", provider.Token);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Action_authorization_requires_the_exact_token()
    {
        var context = new DefaultHttpContext();
        Assert.False(ActionAuthorization.IsAllowed(context.Request, "expected"));

        context.Request.Headers["X-Edge-Token"] = "wrong";
        Assert.False(ActionAuthorization.IsAllowed(context.Request, "expected"));

        context.Request.Headers["X-Edge-Token"] = "expected";
        Assert.True(ActionAuthorization.IsAllowed(context.Request, "expected"));
    }

    [Fact]
    public void Pause_state_round_trips_and_deletes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"edge-pause-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "pause.json");
        var store = new PauseStateStore(path);
        var deadline = DateTimeOffset.UtcNow.AddMinutes(15);
        try
        {
            store.Write(new PauseState(deadline));
            Assert.Equal(deadline, store.Read()?.PausedUntil);

            store.Delete();
            Assert.Null(store.Read());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Nordvpn_module_restores_an_active_pause()
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        var module = new NordVpnModule(
            NullLogger<NordVpnModule>.Instance,
            new StubPauseStateStore(new PauseState(deadline)));

        var status = await module.GetStatusAsync();

        Assert.Equal("paused", status.State);
        Assert.Equal(deadline, status.PausedUntil);
    }

    private sealed class StubPauseStateStore(PauseState? state) : IPauseStateStore
    {
        public PauseState? Read() => state;
        public void Write(PauseState updated) => state = updated;
        public void Delete() => state = null;
    }
}
