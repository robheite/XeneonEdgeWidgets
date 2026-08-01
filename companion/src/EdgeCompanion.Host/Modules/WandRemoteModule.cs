using System.Net;
using System.Text.RegularExpressions;

namespace EdgeCompanion.Host.Modules;

public sealed class WandRemoteModule : IDisposable
{
    private static readonly Uri RemoteBaseUri = new("https://remote.wand.com/");
    private static readonly IReadOnlyDictionary<string, Uri> AllowedUpstreamOrigins = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase)
    {
        ["storage-cdn.wemod.com"] = new("https://storage-cdn.wemod.com/"),
        ["api.wemod.com"] = new("https://api.wemod.com/"),
        ["api-cdn.wemod.com"] = new("https://api-cdn.wemod.com/"),
        ["assistant.wemod.com"] = new("https://assistant.wemod.com/"),
        ["mist.wand.com"] = new("https://mist.wand.com/"),
    };
    private static readonly Regex MetaElement = new(@"<meta\b(?<attributes>[^>]*?)(?<!/)>" , RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CookieDomain = new(@";\s*Domain=[^;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };

    public object Capability() => new { id = "wand-remote", available = true, actions = Array.Empty<string>(), remoteUrl = "http://127.0.0.1:48621/wand/remote/" };

    public async Task ProxyAsync(HttpContext context, string? path, CancellationToken cancellationToken)
    {
        await ProxyAsync(context, BuildUpstreamUri(RemoteBaseUri, path, context.Request.QueryString.Value), cancellationToken);
    }

    public async Task ProxyUpstreamAsync(HttpContext context, string host, string? path, CancellationToken cancellationToken)
    {
        if (!AllowedUpstreamOrigins.TryGetValue(host, out var origin))
            throw new ModuleException("invalid_remote_host", "Invalid Wand Remote host", 400);
        await ProxyAsync(context, BuildUpstreamUri(origin, path, context.Request.QueryString.Value), cancellationToken);
    }

    public static bool IsAllowedUpstreamHost(string host) => AllowedUpstreamOrigins.ContainsKey(host);

    public static string BridgeScript => """
        (function(){
          const hideOrientationPrompt=()=>document.querySelectorAll('orientation-prompt').forEach(element=>element.style.setProperty('display','none','important'));
          new MutationObserver(hideOrientationPrompt).observe(document.documentElement,{childList:true,subtree:true});
          hideOrientationPrompt();
          const mediaOriginal=window.matchMedia.bind(window);
          window.matchMedia=function(query){
            if(query.includes('orientation: portrait'))return {matches:true,media:query,onchange:null,addListener(){},removeListener(){},addEventListener(){},removeEventListener(){},dispatchEvent(){return false}};
            if(query.includes('orientation: landscape'))return {matches:false,media:query,onchange:null,addListener(){},removeListener(){},addEventListener(){},removeEventListener(){},dispatchEvent(){return false}};
            return mediaOriginal(query);
          };
          const hosts=new Set(['storage-cdn.wemod.com','api.wemod.com','mist.wand.com']);
          const toProxy=value=>{const target=new URL(value,location.href);return hosts.has(target.host)?'/wand/upstream/'+target.host+target.pathname+target.search:value};
          const fetchOriginal=window.fetch;
          window.fetch=function(input,init){
            try{
              if(input instanceof Request&&input.method!=='GET'&&input.method!=='HEAD'){
                return input.clone().arrayBuffer().then(body=>fetchOriginal(toProxy(input.url),{method:input.method,headers:input.headers,body:body}));
              }
              if(input instanceof Request)return fetchOriginal(toProxy(input.url),{method:input.method,headers:input.headers});
              return fetchOriginal(toProxy(input),init);
            }catch(error){return fetchOriginal(input,init)}
          };
          const openOriginal=XMLHttpRequest.prototype.open;
          XMLHttpRequest.prototype.open=function(method,url){const args=Array.from(arguments);try{args[1]=toProxy(url)}catch(error){}return openOriginal.apply(this,args)};
        }())
        """;

    private async Task ProxyAsync(HttpContext context, Uri upstream, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), upstream);
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        request.Headers.TryAddWithoutValidation("Accept", context.Request.Headers.Accept.ToString());
        request.Headers.TryAddWithoutValidation("User-Agent", "EdgeCompanion/1.0 WandRemoteProxy");
        request.Headers.TryAddWithoutValidation("Origin", RemoteBaseUri.GetLeftPart(UriPartial.Authority));
        request.Headers.Referrer = RemoteBaseUri;
        if (context.Request.Headers.Authorization.Count > 0)
            request.Headers.TryAddWithoutValidation("Authorization", context.Request.Headers.Authorization.ToString());
        if (context.Request.Headers.Cookie.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", context.Request.Headers.Cookie.ToString());
        if (context.Request.ContentLength is > 0)
        {
            request.Content = new StreamContent(context.Request.Body);
            if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
                request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        context.Response.StatusCode = (int)response.StatusCode;
        CopyHeaders(response, context.Response);
        if (string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(NormalizeHead(html), cancellationToken);
            return;
        }
        await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }

    public static string NormalizeHead(string html)
    {
        var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headEnd < 0) return html;
        var normalizedHead = MetaElement.Replace(html[..headEnd], "<meta${attributes} />");
        const string fetchBridge = "<script src=\"/wand/bridge.js?v=0.1.8\"></script>";
        // Wand deliberately shows this mobile-only overlay in a short landscape viewport.
        // XENEON EDGE is a fixed landscape panel, so it would otherwise block every control.
        const string panelStyle = "<style>orientation-prompt{display:none !important;}</style>";
        return normalizedHead.Replace("<!doctype html>", "<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase) + fetchBridge + panelStyle + html[headEnd..];
    }

    private static Uri BuildUpstreamUri(Uri root, string? path, string? queryString)
    {
        var candidate = new Uri(root, (path ?? string.Empty).TrimStart('/') + queryString);
        if (candidate.Scheme != Uri.UriSchemeHttps || !candidate.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase))
            throw new ModuleException("invalid_remote_path", "Invalid Wand Remote path", 400);
        return candidate;
    }

    private static void CopyHeaders(HttpResponseMessage source, HttpResponse destination)
    {
        foreach (var header in source.Headers.Concat(source.Content.Headers))
        {
            if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var value in header.Value) destination.Headers.Append("Set-Cookie", CookieDomain.Replace(value, string.Empty));
                continue;
            }
            destination.Headers[header.Key] = header.Value.ToArray();
        }
    }

    public void Dispose() => _client.Dispose();
}
