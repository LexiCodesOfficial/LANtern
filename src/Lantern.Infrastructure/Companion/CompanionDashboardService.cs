using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lantern.Application.Abstractions;
using Lantern.Domain;

namespace Lantern.Infrastructure.Companion;

public sealed class CompanionDashboardService : ICompanionDashboardService, IDisposable
{
    private readonly IDeviceRepository _repository;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public CompanionDashboardService(IDeviceRepository repository)
    {
        _repository = repository;
    }

    public bool IsRunning => _listener is not null;
    public string Address { get; private set; } = "Disabled";

    public Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        if (port is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Use a port between 1024 and 65535.");
        }

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Address = $"http://{GetLocalAddress()}:{port}";
        _ = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        Address = "Disabled";
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _ = StopAsync();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => ServeAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
                var devices = await _repository.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
                var isJson = request.StartsWith("GET /api/devices", StringComparison.OrdinalIgnoreCase);
                var body = isJson ? JsonSerializer.Serialize(devices.Select(ToDto)) : BuildHtml(devices);
                var contentType = isJson ? "application/json; charset=utf-8" : "text/html; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(body);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Mobile clients may disconnect while refreshing.
            }
        }
    }

    private static object ToDto(NetworkDevice device) => new
    {
        name = device.DisplayName,
        ipAddress = device.LastIpAddress,
        hostname = device.Hostname,
        vendor = device.Vendor,
        type = device.DeviceType.ToString(),
        status = device.Status.ToString(),
        lastSeenUtc = device.LastSeenUtc
    };

    private static string BuildHtml(IReadOnlyList<NetworkDevice> devices)
    {
        var cards = string.Join("", devices.OrderByDescending(device => device.Status == DeviceStatus.Online).ThenBy(device => device.DisplayName).Select(device =>
            $"<article><b>{HtmlEncoder.Default.Encode(device.DisplayName)}</b><span>{HtmlEncoder.Default.Encode(device.Status.ToString())}</span><small>{HtmlEncoder.Default.Encode(device.LastIpAddress ?? "No IP")} · {HtmlEncoder.Default.Encode(device.Vendor ?? "Unknown vendor")} · {HtmlEncoder.Default.Encode(device.DeviceType.ToString())}</small></article>"));
        return $$"""
            <!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>LANtern Companion</title><style>
            body{font-family:system-ui;background:#0b1220;color:#f8fafc;margin:0;padding:18px}header{margin-bottom:18px}h1{margin:0}p,small{color:#aab6c8}
            main{display:grid;gap:10px}article{display:grid;grid-template-columns:1fr auto;gap:6px;background:#111a2c;padding:14px;border-radius:8px}
            small{grid-column:1/3}span{color:#32d583}a{color:#84caff}</style></head>
            <body><header><h1>LANtern</h1><p>Read-only local network companion · {{devices.Count}} remembered devices</p></header><main>{{cards}}</main></body></html>
            """;
    }

    private static string GetLocalAddress()
        => Dns.GetHostEntry(Dns.GetHostName()).AddressList
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))?.ToString()
            ?? "127.0.0.1";
}
