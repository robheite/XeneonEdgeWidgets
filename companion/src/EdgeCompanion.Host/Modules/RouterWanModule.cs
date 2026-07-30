using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace EdgeCompanion.Host.Modules;

public sealed record RouterWanResult(string? Ip, string? Source, string Status);
public sealed record GatewayCandidate(IPAddress LocalAddress, IPAddress SubnetMask, IPAddress Gateway);

public sealed class RouterWanModule(ILogger<RouterWanModule> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private RouterWanResult? _cached;
    private DateTimeOffset _cachedUntil;

    public object Capability() => new
    {
        id = "router-wan",
        available = true,
        status = "available",
        providers = new[] { "upnp-igd", "nat-pmp" },
    };

    public async Task<RouterWanResult> GetAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null && _cachedUntil > DateTimeOffset.UtcNow)
            return _cached;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && _cachedUntil > DateTimeOffset.UtcNow)
                return _cached;

            var gateways = FindGateways().ToArray();
            foreach (var gateway in gateways)
            {
                try
                {
                    var upnp = await UpnpIgdDiscovery.TryGetExternalAddressAsync(gateway, cancellationToken);
                    if (upnp is not null)
                        return Cache(new(upnp.ToString(), "UPnP IGD", "available"), TimeSpan.FromMinutes(5));
                }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogDebug(exception, "UPnP IGD timed out through {Gateway}", gateway.Gateway);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogDebug(exception, "UPnP IGD did not return a WAN address through {Gateway}", gateway.Gateway);
                }
            }

            foreach (var gateway in gateways)
            {
                try
                {
                    var natPmp = await NatPmpDiscovery.TryGetExternalAddressAsync(gateway, cancellationToken);
                    if (natPmp is not null)
                        return Cache(new(natPmp.ToString(), "NAT-PMP", "available"), TimeSpan.FromMinutes(5));
                }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogDebug(exception, "NAT-PMP timed out through {Gateway}", gateway.Gateway);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogDebug(exception, "NAT-PMP did not return a WAN address through {Gateway}", gateway.Gateway);
                }
            }

            return Cache(new(null, null, "unavailable"), TimeSpan.FromSeconds(30));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "WAN discovery failed");
            return Cache(new(null, null, "unavailable"), TimeSpan.FromSeconds(30));
        }
        finally
        {
            _lock.Release();
        }
    }

    private RouterWanResult Cache(RouterWanResult result, TimeSpan duration)
    {
        _cached = result;
        _cachedUntil = DateTimeOffset.UtcNow.Add(duration);
        return result;
    }

    public static IEnumerable<GatewayCandidate> FindGateways()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(item => item.OperationalStatus == OperationalStatus.Up
                         && item.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                         and not NetworkInterfaceType.Tunnel))
        {
            var properties = adapter.GetIPProperties();
            var gateway = properties.GatewayAddresses
                .Select(item => item.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            if (gateway is null) continue;

            foreach (var address in properties.UnicastAddresses.Where(item =>
                         item.Address.AddressFamily == AddressFamily.InterNetwork
                         && item.IPv4Mask is not null))
            {
                yield return new(address.Address, address.IPv4Mask, gateway);
            }
        }
    }
}

public static class UpnpIgdDiscovery
{
    private static readonly string[] SearchTargets =
    [
        "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
        "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
    ];

    public static async Task<IPAddress?> TryGetExternalAddressAsync(
        GatewayCandidate candidate,
        CancellationToken cancellationToken)
    {
        foreach (var location in await DiscoverLocationsAsync(candidate, cancellationToken))
        {
            var address = await QueryLocationAsync(location, candidate, cancellationToken);
            if (address is not null) return address;
        }

        return null;
    }

    public static Uri? ParseLocation(string response)
    {
        foreach (var line in response.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator < 0 || !line[..separator].Trim().Equals("location", StringComparison.OrdinalIgnoreCase))
                continue;
            return Uri.TryCreate(line[(separator + 1)..].Trim(), UriKind.Absolute, out var uri) ? uri : null;
        }
        return null;
    }

    public static (string ServiceType, Uri ControlUrl)? ParseService(
        string descriptionXml,
        Uri descriptionUrl,
        GatewayCandidate candidate)
    {
        var document = XDocument.Parse(descriptionXml, LoadOptions.None);
        foreach (var service in document.Descendants().Where(element => element.Name.LocalName == "service"))
        {
            var serviceType = service.Elements().FirstOrDefault(element => element.Name.LocalName == "serviceType")?.Value;
            var controlUrl = service.Elements().FirstOrDefault(element => element.Name.LocalName == "controlURL")?.Value;
            if (string.IsNullOrWhiteSpace(serviceType)
                || string.IsNullOrWhiteSpace(controlUrl)
                || (!serviceType.Contains(":WANIPConnection:", StringComparison.Ordinal)
                    && !serviceType.Contains(":WANPPPConnection:", StringComparison.Ordinal)))
                continue;

            var resolved = new Uri(descriptionUrl, controlUrl);
            if (IsAllowedLanUrl(resolved, candidate))
                return (serviceType, resolved);
        }
        return null;
    }

    public static IPAddress? ParseExternalAddress(string responseXml)
    {
        var value = XDocument.Parse(responseXml, LoadOptions.None)
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "NewExternalIPAddress")
            ?.Value;
        return IPAddress.TryParse(value, out var address) && PublicIpv4.IsGloballyRoutable(address)
            ? address
            : null;
    }

    public static bool IsAllowedLanUrl(Uri uri, GatewayCandidate candidate)
    {
        if (uri.Scheme != Uri.UriSchemeHttp || !IPAddress.TryParse(uri.Host, out var host))
            return false;
        return host.Equals(candidate.Gateway)
            || NetworkAddress.IsSameSubnet(candidate.LocalAddress, host, candidate.SubnetMask);
    }

    private static async Task<IReadOnlyCollection<Uri>> DiscoverLocationsAsync(
        GatewayCandidate candidate,
        CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(candidate.LocalAddress, 0));
        udp.Client.SetSocketOption(
            SocketOptionLevel.IP,
            SocketOptionName.MulticastInterface,
            candidate.LocalAddress.GetAddressBytes());

        var endpoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        foreach (var searchTarget in SearchTargets)
        {
            var request = Encoding.ASCII.GetBytes(
                "M-SEARCH * HTTP/1.1\r\n" +
                "HOST: 239.255.255.250:1900\r\n" +
                "MAN: \"ssdp:discover\"\r\n" +
                "MX: 1\r\n" +
                $"ST: {searchTarget}\r\n\r\n");
            await udp.SendAsync(request, endpoint, cancellationToken);
        }

        var locations = new HashSet<Uri>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(900));
        try
        {
            while (true)
            {
                var response = await udp.ReceiveAsync(timeout.Token);
                var location = ParseLocation(Encoding.UTF8.GetString(response.Buffer));
                if (location is not null && IsAllowedLanUrl(location, candidate))
                    locations.Add(location);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        return locations;
    }

    private static async Task<IPAddress?> QueryLocationAsync(
        Uri location,
        GatewayCandidate candidate,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(2),
            MaxResponseContentBufferSize = 256 * 1024,
        };
        var description = await client.GetStringAsync(location, cancellationToken);
        var service = ParseService(description, location, candidate);
        if (service is null) return null;

        const string bodyTemplate = """
            <?xml version="1.0"?>
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
              <s:Body>
                <u:GetExternalIPAddress xmlns:u="{0}" />
              </s:Body>
            </s:Envelope>
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, service.Value.ControlUrl);
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{service.Value.ServiceType}#GetExternalIPAddress\"");
        request.Content = new StringContent(string.Format(bodyTemplate, service.Value.ServiceType), Encoding.UTF8, "text/xml");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return ParseExternalAddress(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}

public static class NatPmpDiscovery
{
    public static async Task<IPAddress?> TryGetExternalAddressAsync(
        GatewayCandidate candidate,
        CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(new IPEndPoint(candidate.LocalAddress, 0));
        udp.Connect(candidate.Gateway, 5351);
        await udp.SendAsync(new byte[] { 0, 0 }.AsMemory(), cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(900));
        try
        {
            var response = await udp.ReceiveAsync(timeout.Token);
            return ParsePublicAddressResponse(response.Buffer);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public static IPAddress? ParsePublicAddressResponse(ReadOnlySpan<byte> response)
    {
        if (response.Length < 12
            || response[0] != 0
            || response[1] != 128
            || response[2] != 0
            || response[3] != 0)
            return null;
        var address = new IPAddress(response[8..12]);
        return PublicIpv4.IsGloballyRoutable(address) ? address : null;
    }
}

public static class NetworkAddress
{
    public static bool IsSameSubnet(IPAddress left, IPAddress right, IPAddress mask)
    {
        var leftBytes = left.GetAddressBytes();
        var rightBytes = right.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        if (leftBytes.Length != 4 || rightBytes.Length != 4 || maskBytes.Length != 4)
            return false;
        return Enumerable.Range(0, 4).All(index =>
            (leftBytes[index] & maskBytes[index]) == (rightBytes[index] & maskBytes[index]));
    }
}

public static class PublicIpv4
{
    public static bool IsGloballyRoutable(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return false;
        return bytes[0] is not 0 and not 10 and not 127
            && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            && !(bytes[0] == 169 && bytes[1] == 254)
            && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2)
            && !(bytes[0] == 192 && bytes[1] == 168)
            && !(bytes[0] == 198 && bytes[1] is 18 or 19)
            && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
            && bytes[0] < 224
            && !(bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255);
    }
}
