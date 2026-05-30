using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Application.Utilities;

namespace Lantern.Infrastructure.NetworkScanning.Discovery;

public sealed class SsdpDiscoveryProvider : IDiscoveryProvider
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("239.255.255.250"), 1900);
    public string Name => "SSDP";

    public async IAsyncEnumerable<DiscoveredDevice> DiscoverAsync(
        NetworkScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var request = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: ssdp:all\r\n\r\n");
        await udp.SendAsync(request, MulticastEndpoint, cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(950));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!timeout.IsCancellationRequested)
        {
            UdpReceiveResult response;
            try
            {
                response = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (SocketException)
            {
                yield break;
            }

            var address = response.RemoteEndPoint.Address;
            if (!PrivateIpRangeHelper.IsPrivateIPv4(address) || !seen.Add(address.ToString()))
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(response.Buffer);
            yield return new DiscoveredDevice
            {
                IpAddress = address.ToString(),
                DiscoverySource = text.Contains("LOCATION:", StringComparison.OrdinalIgnoreCase) ? $"{Name} Service" : Name,
                ObservedUtc = DateTimeOffset.UtcNow
            };
        }
    }
}
