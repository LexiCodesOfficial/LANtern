using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Application.Utilities;

namespace Lantern.Infrastructure.NetworkScanning.Discovery;

public sealed class TcpProbeDiscoveryProvider : IDiscoveryProvider
{
    public string Name => "TCP Probe";

    public async IAsyncEnumerable<DiscoveredDevice> DiscoverAsync(
        NetworkScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!context.Options.EnableTcpDiscovery)
        {
            yield break;
        }

        var channel = Channel.CreateBounded<DiscoveredDevice>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        _ = Task.Run(async () =>
        {
            using var semaphore = new SemaphoreSlim(context.Options.TcpDiscoveryConcurrency);
            var tasks = context.CandidateAddresses.Select(async address =>
            {
                if (!context.Options.AllowPublicRanges && !PrivateIpRangeHelper.IsPrivateIPv4(address))
                {
                    return;
                }

                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    foreach (var port in context.Options.TcpDiscoveryPorts)
                    {
                        if (await CanConnectAsync(address, port, context.Options.TcpConnectTimeout, cancellationToken).ConfigureAwait(false))
                        {
                            await channel.Writer.WriteAsync(new DiscoveredDevice
                            {
                                IpAddress = address.ToString(),
                                DiscoverySource = Name,
                                OpenPorts =
                                [
                                    new OpenPortInfo(port, NetworkPortNames.GetServiceName(port), DateTimeOffset.UtcNow)
                                ],
                                ObservedUtc = DateTimeOffset.UtcNow
                            }, cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
            }
        }, cancellationToken);

        await foreach (var device in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return device;
        }
    }

    private static async Task<bool> CanConnectAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(address, port, cancellationToken).AsTask();
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeout, CancellationToken.None)).ConfigureAwait(false);
            return completed == connectTask && client.Connected;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
