using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EdgeCompanion.Host.Modules;

public sealed class EmbyModule(IHttpClientFactory httpClientFactory)
{
    public object Capability() => new { id = "emby", available = true };

    public async Task<HttpResponseMessage> SendAsync(
        string serverUrl,
        HttpMethod method,
        string path,
        string? accessToken,
        HttpRequest? sourceRequest,
        object? body,
        CancellationToken cancellationToken)
    {
        var baseUri = ValidateServer(serverUrl);
        var request = new HttpRequestMessage(method, new Uri(baseUri, $"emby/{path.TrimStart('/')}"));
        request.Headers.TryAddWithoutValidation("X-Emby-Authorization", AuthorizationHeader(accessToken));
        if (!string.IsNullOrWhiteSpace(accessToken)) request.Headers.TryAddWithoutValidation("X-Emby-Token", accessToken);
        if (sourceRequest?.Headers.Range.Count > 0)
            request.Headers.Range = RangeHeaderValue.Parse(sourceRequest.Headers.Range.ToString());
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await httpClientFactory.CreateClient("emby").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public static Uri ValidateServer(string serverUrl)
    {
        if (!Uri.TryCreate(serverUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !IsLocalHost(uri.Host))
            throw new ModuleException("invalid_emby_server", "Emby server must use a local or private network address", 400);
        return uri;
    }

    public static bool IsLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!System.Net.IPAddress.TryParse(host, out var ip)) return false;
        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31);
        return ip.Equals(System.Net.IPAddress.IPv6Loopback) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;
    }

    public static string BuildVideoStreamPath(
        string itemId,
        string mediaSourceId,
        string playSessionId,
        int? audioStreamIndex,
        int? subtitleStreamIndex,
        long? startTimeTicks = null)
    {
        var query = new List<string>
        {
            $"MediaSourceId={Uri.EscapeDataString(mediaSourceId)}",
            $"PlaySessionId={Uri.EscapeDataString(playSessionId)}",
            "DeviceId=edge-emby-player",
        };

        if (audioStreamIndex is not null) query.Add($"AudioStreamIndex={audioStreamIndex}");
        if (subtitleStreamIndex is not null)
        {
            query.Add($"SubtitleStreamIndex={subtitleStreamIndex}");
            query.Add("SubtitleMethod=Encode");
        }
        if (startTimeTicks is > 0) query.Add($"StartTimeTicks={startTimeTicks}");

        query.AddRange([
            "VideoCodec=vpx",
            "AudioCodec=vorbis",
            "VideoBitRate=6000000",
            "AudioBitRate=192000",
            "MaxAudioChannels=2",
            "AudioSampleRate=48000",
            "MaxWidth=1920",
            "MaxHeight=1080",
            "MaxVideoBitDepth=8",
            "EnableAutoStreamCopy=false",
        ]);

        return $"Videos/{Uri.EscapeDataString(itemId)}/stream.webm?{string.Join("&", query)}";
    }

    private static string AuthorizationHeader(string? token) =>
        $"Emby UserId=\"\", Client=\"XENEON EDGE\", Device=\"XENEON EDGE\", DeviceId=\"edge-emby-player\", Version=\"1.0.0\"{(string.IsNullOrWhiteSpace(token) ? "" : $", Token=\"{token}\"")}";
}

public sealed record EmbyAuthRequest(string ServerUrl, string Username, string Password);
public sealed record EmbyPlaybackRequest(string ServerUrl, string AccessToken, JsonElement Playback);
public sealed record EmbyWatchedRequest(string ServerUrl, string AccessToken, bool Played);
