using System.Net;
using System.Net.Sockets;
using Lantern.Application;
using Lantern.Domain;
using Lantern.Infrastructure.Companion;
using Lantern.Infrastructure.NetworkScanning.Discovery;
using Lantern.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

var tempDirectory = Path.Combine(Path.GetTempPath(), $"lantern-smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDirectory);

try
{
    await VerifyRepositoryAndCompanionAsync();
    VerifyArpParsing();
    Console.WriteLine("LANtern smoke tests passed.");
}
finally
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(tempDirectory, true);
}

async Task VerifyRepositoryAndCompanionAsync()
{
    var repository = new SqliteDeviceRepository(Path.Combine(tempDirectory, "lantern.db"));
    var device = new NetworkDevice
    {
        FriendlyName = "Kitchen Tablet",
        LocationLabel = "Kitchen",
        LastIpAddress = "192.168.1.20",
        FirstSeenUtc = DateTimeOffset.UtcNow,
        LastSeenUtc = DateTimeOffset.UtcNow,
        Status = DeviceStatus.Online
    };
    await repository.SaveDeviceAsync(device);
    using var companion = new CompanionDashboardService(repository);
    var port = ReservePort();
    await companion.StartAsync(port);
    using var client = new HttpClient();
    var html = await client.GetStringAsync($"http://127.0.0.1:{port}/");
    var json = await client.GetStringAsync($"http://127.0.0.1:{port}/api/devices");
    Assert(html.Contains("Kitchen Tablet", StringComparison.Ordinal), "Companion HTML did not include the remembered device.");
    Assert(json.Contains("Kitchen Tablet", StringComparison.Ordinal), "Companion JSON did not include the remembered device.");
}

void VerifyArpParsing()
{
    var entries = ArpDiscoveryProvider.Parse("  192.168.1.20          bc-24-11-aa-bb-cc     dynamic");
    Assert(entries.Count == 1 && entries[0].MacAddress == "BC:24:11:AA:BB:CC", "ARP parsing did not normalize a Windows MAC address.");
}

static int ReservePort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
