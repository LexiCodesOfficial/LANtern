using System.Net;
using System.Net.Http;
using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Application.Utilities;

namespace Lantern.Infrastructure.Integrations;

public sealed class LocalIntegrationService : ILocalIntegrationService
{
    private readonly IDeviceRepository _repository;

    public LocalIntegrationService(IDeviceRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<IntegrationSummary>> InspectAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var device = await _repository.GetDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);
        if (device is null)
        {
            return [];
        }

        var ports = (await _repository.GetOpenPortsAsync(deviceId, cancellationToken).ConfigureAwait(false)).ToHashSet();
        var results = new List<IntegrationSummary>();
        await AddAsync(8006, "Proxmox", "A Proxmox web console was observed. API inventory can be enabled later with a read-only token.", "https", "/api2/json/version");
        await AddAsync(9443, "Docker / Portainer", "A Portainer-compatible secure web endpoint was observed. Container details can be added later with a read-only token.", "https", "/api/status");
        await AddAsync(8123, "Home Assistant", "A Home Assistant dashboard was observed. Entity summaries can be enabled later with a long-lived read-only token.", "http", "/api/");
        if (device.Vendor?.Contains("MikroTik", StringComparison.OrdinalIgnoreCase) == true)
        {
            results.Add(new IntegrationSummary("MikroTik", "Detected", "RouterOS network equipment detected. DHCP hostname import is available from Settings.", device.LastIpAddress));
        }

        return results.Count == 0
            ? [new IntegrationSummary("Local services", "No module detected", "LANtern did not find a supported local integration endpoint on this device.")]
            : results;

        async Task AddAsync(int port, string name, string explanation, string scheme, string path)
        {
            if (!ports.Contains(port) || !IPAddress.TryParse(device.LastIpAddress, out var ipAddress) || !PrivateIpRangeHelper.IsPrivateIPv4(ipAddress))
            {
                return;
            }

            var address = $"{scheme}://{device.LastIpAddress}:{port}";
            var responds = await RespondsAsync($"{address}{path}", cancellationToken).ConfigureAwait(false);
            results.Add(new IntegrationSummary(name, responds ? "Responding" : "Port observed", explanation, address));
        }
    }

    private static async Task<bool> RespondsAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(1.2) };
            using var response = await client.GetAsync(address, cancellationToken).ConfigureAwait(false);
            return (int)response.StatusCode is >= 200 and < 500;
        }
        catch
        {
            return false;
        }
    }
}
