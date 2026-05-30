using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Application.Utilities;

namespace Lantern.Infrastructure.NetworkScanning.Enrichment;

public sealed class PortProbeEnrichmentProvider : IEnrichmentProvider
{
    public string Name => "Ports";

    public async Task<DiscoveredDevice> EnrichAsync(
        DiscoveredDevice device,
        NetworkScanContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.EnablePortEnrichment || !IPAddress.TryParse(device.IpAddress, out var address))
        {
            return device;
        }

        if (!context.Options.AllowPublicRanges && !PrivateIpRangeHelper.IsPrivateIPv4(address))
        {
            return device;
        }

        var open = new ConcurrentBag<OpenPortInfo>();
        using var semaphore = new SemaphoreSlim(context.Options.PortEnrichmentConcurrency);

        await Task.WhenAll(context.Options.EnrichmentPorts.Select(async port =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (await CanConnectAsync(address, port, context.Options.TcpConnectTimeout, cancellationToken).ConfigureAwait(false))
                {
                    var serviceName = NetworkPortNames.GetServiceName(port);
                    if (port == 9443)
                    {
                        if (!await IsPortainerAsync(address, port, context.Options.TcpConnectTimeout, cancellationToken).ConfigureAwait(false))
                        {
                            serviceName = "HTTPS Alternate";
                        }
                    }

                    open.Add(new OpenPortInfo(port, serviceName, DateTimeOffset.UtcNow));
                }
            }
            finally
            {
                semaphore.Release();
            }
        })).ConfigureAwait(false);

        if (open.IsEmpty)
        {
            return device;
        }

        var mergedPorts = device.OpenPorts.Concat(open)
            .GroupBy(port => port.Port)
            .Select(group => group.OrderByDescending(port => port.ObservedUtc).First())
            .OrderBy(port => port.Port)
            .ToArray();

        return device with { OpenPorts = mergedPorts };
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

    private static async Task<bool> IsPortainerAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            using var client = new HttpClient(handler)
            {
                Timeout = timeout < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : timeout
            };
            using var response = await client.GetAsync($"https://{address}:{port}/#!/auth", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            return string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                || content.Contains("<html", StringComparison.OrdinalIgnoreCase)
                || content.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
