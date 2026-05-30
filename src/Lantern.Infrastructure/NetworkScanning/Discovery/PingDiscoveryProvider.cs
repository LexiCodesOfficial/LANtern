using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Lantern.Application;
using Lantern.Application.Abstractions;

namespace Lantern.Infrastructure.NetworkScanning.Discovery;

public sealed class PingDiscoveryProvider : IDiscoveryProvider
{
    public string Name => "Ping";

    public async IAsyncEnumerable<DiscoveredDevice> DiscoverAsync(
        NetworkScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<DiscoveredDevice>(new BoundedChannelOptions(128)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        _ = Task.Run(async () =>
        {
            using var semaphore = new SemaphoreSlim(context.Options.PingConcurrency);
            var tasks = context.CandidateAddresses.Select(async address =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (await IsAliveAsync(address.ToString(), context.Options.PingTimeout).ConfigureAwait(false))
                    {
                        await channel.Writer.WriteAsync(new DiscoveredDevice
                        {
                            IpAddress = address.ToString(),
                            DiscoverySource = Name,
                            ObservedUtc = DateTimeOffset.UtcNow
                        }, cancellationToken).ConfigureAwait(false);
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

    private static async Task<bool> IsAliveAsync(string ipAddress, TimeSpan timeout)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, (int)timeout.TotalMilliseconds).ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}
