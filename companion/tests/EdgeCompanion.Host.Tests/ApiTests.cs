using EdgeCompanion.Host;
using EdgeCompanion.Host.Modules;

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
}
