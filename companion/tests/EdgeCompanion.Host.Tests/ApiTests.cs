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
    public void Local_widget_origins_are_allowed(string origin)
    {
        Assert.True(OriginPolicy.IsAllowed(origin));
    }

    [Fact]
    public void Wand_proxy_origin_cannot_receive_privileged_cors_access()
    {
        Assert.False(OriginPolicy.IsAllowed("http://localhost:48620"));
    }

    [Fact]
    public void Wand_proxy_only_allows_known_wand_service_hosts()
    {
        Assert.True(WandRemoteModule.IsAllowedUpstreamHost("api.wemod.com"));
        Assert.True(WandRemoteModule.IsAllowedUpstreamHost("mist.wand.com"));
        Assert.False(WandRemoteModule.IsAllowedUpstreamHost("example.com"));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("not a URI")]
    [InlineData("")]
    public void Non_local_origins_are_rejected(string origin)
    {
        Assert.False(OriginPolicy.IsAllowed(origin));
    }

    [Theory]
    [InlineData("192.168.1.11", true)]
    [InlineData("10.0.0.4", true)]
    [InlineData("172.20.1.2", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("emby.example.com", false)]
    public void Emby_proxy_only_accepts_local_network_hosts(string host, bool expected)
    {
        Assert.Equal(expected, EmbyModule.IsLocalHost(host));
    }

    [Fact]
    public void Emby_server_validation_rejects_public_hosts()
    {
        var exception = Assert.Throws<ModuleException>(() => EmbyModule.ValidateServer("https://example.com"));
        Assert.Equal("invalid_emby_server", exception.Code);
    }

    [Fact]
    public void Emby_transcode_path_forces_icue_supported_video_and_audio()
    {
        var path = EmbyModule.BuildVideoStreamPath("item 1", "source 2", "session 3", 4, 5, 123456789);

        Assert.StartsWith("Videos/item%201/stream.webm?", path);
        Assert.Contains("VideoCodec=vpx", path);
        Assert.Contains("AudioCodec=vorbis", path);
        Assert.Contains("MaxVideoBitDepth=8", path);
        Assert.Contains("MaxAudioChannels=2", path);
        Assert.Contains("AudioStreamIndex=4", path);
        Assert.Contains("SubtitleStreamIndex=5", path);
        Assert.Contains("SubtitleMethod=Encode", path);
        Assert.Contains("StartTimeTicks=123456789", path);
        Assert.DoesNotContain("Static=true", path);
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
    public void Wand_remote_normalizes_void_head_elements_without_touching_the_body()
    {
        const string html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Wand Remote</title></head><body><meta data-body=\"kept\"></body></html>";
        var normalized = WandRemoteModule.NormalizeHead(html);
        Assert.Contains("<meta charset=\"utf-8\" />", normalized);
        Assert.StartsWith("<!DOCTYPE html>", normalized);
        Assert.Contains("/wand/bridge.js?v=0.1.8", normalized);
        Assert.Contains("orientation-prompt{display:none !important;}", normalized);
        Assert.Contains("<body><meta data-body=\"kept\"></body>", normalized);
    }

    [Fact]
    public void Wand_remote_bridge_hides_orientation_prompts_added_after_page_load()
    {
        Assert.Contains("MutationObserver", WandRemoteModule.BridgeScript);
        Assert.Contains("querySelectorAll('orientation-prompt')", WandRemoteModule.BridgeScript);
        Assert.Contains("setProperty('display','none','important')", WandRemoteModule.BridgeScript);
        Assert.Contains("input.clone().arrayBuffer()", WandRemoteModule.BridgeScript);
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
