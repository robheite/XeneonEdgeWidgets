using System.Net;
using EdgeCompanion.Host.Modules;

namespace EdgeCompanion.Host.Tests;

public class WanAndStartupTests
{
    private static readonly GatewayCandidate Candidate = new(
        IPAddress.Parse("192.168.1.20"),
        IPAddress.Parse("255.255.255.0"),
        IPAddress.Parse("192.168.1.1"));

    [Fact]
    public void Upnp_parses_location_header_case_insensitively()
    {
        var result = UpnpIgdDiscovery.ParseLocation(
            "HTTP/1.1 200 OK\r\nLOCATION: http://192.168.1.1:5000/root.xml\r\n\r\n");

        Assert.Equal("http://192.168.1.1:5000/root.xml", result?.ToString());
    }

    [Fact]
    public void Upnp_resolves_supported_local_wan_service()
    {
        const string description = """
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device><serviceList><service>
                <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
                <controlURL>/upnp/control/wanip</controlURL>
              </service></serviceList></device>
            </root>
            """;

        var result = UpnpIgdDiscovery.ParseService(
            description,
            new Uri("http://192.168.1.1:5000/root.xml"),
            Candidate);

        Assert.Equal("urn:schemas-upnp-org:service:WANIPConnection:1", result?.ServiceType);
        Assert.Equal("http://192.168.1.1:5000/upnp/control/wanip", result?.ControlUrl.ToString());
    }

    [Fact]
    public void Upnp_rejects_control_urls_outside_the_lan()
    {
        const string description = """
            <root><service>
              <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
              <controlURL>http://198.51.100.10/control</controlURL>
            </service></root>
            """;

        Assert.Null(UpnpIgdDiscovery.ParseService(
            description,
            new Uri("http://192.168.1.1/root.xml"),
            Candidate));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("198.51.100.10", false)]
    [InlineData("203.0.113.10", false)]
    public void Wan_results_must_be_globally_routable(string value, bool expected)
    {
        Assert.Equal(expected, PublicIpv4.IsGloballyRoutable(IPAddress.Parse(value)));
    }

    [Fact]
    public void Upnp_reads_public_address_from_soap_response()
    {
        const string response = """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
              <s:Body><u:GetExternalIPAddressResponse xmlns:u="urn:schemas-upnp-org:service:WANIPConnection:1">
                <NewExternalIPAddress>8.8.4.4</NewExternalIPAddress>
              </u:GetExternalIPAddressResponse></s:Body>
            </s:Envelope>
            """;

        Assert.Equal(IPAddress.Parse("8.8.4.4"), UpnpIgdDiscovery.ParseExternalAddress(response));
    }

    [Fact]
    public void Nat_pmp_reads_successful_public_address_response()
    {
        byte[] response = [0, 128, 0, 0, 0, 0, 0, 1, 8, 8, 8, 8];

        Assert.Equal(IPAddress.Parse("8.8.8.8"), NatPmpDiscovery.ParsePublicAddressResponse(response));
    }

    [Theory]
    [InlineData(new byte[] { 0, 128, 0, 2, 0, 0, 0, 1, 8, 8, 8, 8 })]
    [InlineData(new byte[] { 0, 128, 0, 0, 0, 0, 0, 1, 192, 168, 1, 1 })]
    [InlineData(new byte[] { 0, 128 })]
    public void Nat_pmp_rejects_errors_private_addresses_and_short_responses(byte[] response)
    {
        Assert.Null(NatPmpDiscovery.ParsePublicAddressResponse(response));
    }

    [Fact]
    public void Startup_module_adds_and_removes_exact_current_user_command()
    {
        var store = new MemoryStartupStore();
        var module = new StartupModule(store, @"C:\Apps\Edge Companion\EdgeCompanion.Host.exe");

        var enabled = module.Set(true);
        Assert.True(enabled.Enabled);
        Assert.Equal("\"C:\\Apps\\Edge Companion\\EdgeCompanion.Host.exe\"", store.Value);

        var disabled = module.Set(false);
        Assert.False(disabled.Enabled);
        Assert.Null(store.Value);
    }

    [Fact]
    public void Startup_module_rejects_development_output()
    {
        var module = new StartupModule(
            new MemoryStartupStore(),
            @"C:\src\companion\bin\Debug\net8.0\EdgeCompanion.Host.exe");

        var result = module.Get();

        Assert.False(result.Supported);
        Assert.Equal("unsupported_install", result.Status);
    }

    private sealed class MemoryStartupStore : IStartupStore
    {
        public string? Value { get; private set; }
        public string? Read() => Value;
        public void Write(string command) => Value = command;
        public void Delete() => Value = null;
    }
}
