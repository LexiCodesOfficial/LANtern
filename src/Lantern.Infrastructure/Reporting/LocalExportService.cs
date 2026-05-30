using System.Globalization;
using System.Text;
using Lantern.Application.Abstractions;
using Lantern.Domain;

namespace Lantern.Infrastructure.Reporting;

public sealed class LocalExportService : IExportService
{
    private readonly IDeviceRepository _repository;

    public LocalExportService(IDeviceRepository repository)
    {
        _repository = repository;
    }

    public async Task<string> ExportDevicesCsvAsync(CancellationToken cancellationToken = default)
    {
        var devices = await _repository.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<IReadOnlyList<string?>>
        {
            new[] { "Name", "Label", "Hostname", "IP address", "MAC address", "Vendor", "Type", "Status", "First seen UTC", "Last seen UTC", "Notes" }
        };
        rows.AddRange(devices.Select(device => new[]
        {
            device.DisplayName, device.LocationLabel, device.Hostname, device.LastIpAddress, device.MacAddress, device.Vendor,
            device.DeviceType.ToString(), device.Status.ToString(), Format(device.FirstSeenUtc), Format(device.LastSeenUtc), device.Notes
        }));
        return await WriteCsvAsync("devices", rows, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ExportTimelineCsvAsync(CancellationToken cancellationToken = default)
    {
        var events = await _repository.GetEventsAsync(take: 10_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        var rows = new List<IReadOnlyList<string?>>
        {
            new[] { "Occurred UTC", "Type", "Title", "Description", "Device ID" }
        };
        rows.AddRange(events.Select(networkEvent => new[]
        {
            Format(networkEvent.OccurredUtc), networkEvent.Kind.ToString(), networkEvent.Title, networkEvent.Description, networkEvent.DeviceId?.ToString()
        }));
        return await WriteCsvAsync("timeline", rows, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ExportPdfReportAsync(CancellationToken cancellationToken = default)
    {
        var devices = await _repository.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        var events = await _repository.GetEventsAsync(take: 12, cancellationToken: cancellationToken).ConfigureAwait(false);
        var lines = new List<string>
        {
            "LANtern Local Network Report",
            $"Created: {DateTimeOffset.Now:g}",
            $"Remembered devices: {devices.Count}",
            $"Online devices: {devices.Count(device => device.Status == DeviceStatus.Online)}",
            "",
            "Devices"
        };
        lines.AddRange(devices.Take(36).Select(device =>
            $"{device.DisplayName} | {device.LastIpAddress ?? "No IP"} | {device.Vendor ?? "Unknown vendor"} | {Friendly(device.DeviceType)} | {device.Status}"));
        lines.Add("");
        lines.Add("Recent changes");
        lines.AddRange(events.Select(networkEvent => $"{networkEvent.OccurredUtc.ToLocalTime():g} | {networkEvent.Title} | {networkEvent.Description}"));

        var path = NewExportPath("network-report", ".pdf");
        await File.WriteAllBytesAsync(path, SimplePdfWriter.Create(lines), cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static async Task<string> WriteCsvAsync(string name, IEnumerable<IReadOnlyList<string?>> rows, CancellationToken cancellationToken)
    {
        var path = NewExportPath(name, ".csv");
        var csv = string.Join(Environment.NewLine, rows.Select(row => string.Join(",", row.Select(Escape))));
        await File.WriteAllTextAsync(path, csv, new UTF8Encoding(true), cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static string NewExportPath(string name, string extension)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LANtern Exports");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{name}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}{extension}");
    }

    private static string Escape(string? value)
        => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static string Format(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string Friendly(DeviceType type)
        => type.ToString().Replace("Pc", " PC").Replace("Tv", " TV").Replace("Server", " Server").Replace("Device", " Device");
}

internal static class SimplePdfWriter
{
    public static byte[] Create(IReadOnlyList<string> lines)
    {
        var content = new StringBuilder("BT\n/F1 10 Tf\n48 760 Td\n");
        foreach (var line in lines.Take(62))
        {
            content.Append('(').Append(Escape(line)).Append(") Tj\n0 -12 Td\n");
        }
        content.Append("ET");

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            pdf.Append(offset.ToString("0000000000", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }
        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", " ").Replace("\n", " ");
}
