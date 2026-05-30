using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Application.Utilities;

namespace Lantern.Infrastructure.NetworkScanning.Discovery;

public sealed class MdnsServiceDiscoveryProvider : IDiscoveryProvider
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("224.0.0.251"), 5353);
    public string Name => "mDNS Service";

    public async IAsyncEnumerable<DiscoveredDevice> DiscoverAsync(
        NetworkScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var query = BuildServicesQuery();
        await udp.SendAsync(query, MulticastEndpoint, cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(850));
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

            yield return new DiscoveredDevice
            {
                IpAddress = address.ToString(),
                DiscoverySource = Name,
                ObservedUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private static byte[] BuildServicesQuery()
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        foreach (var label in "_services._dns-sd._udp.local".Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        stream.Write(new byte[] { 0, 0, 12, 0, 1 });
        return stream.ToArray();
    }
}
