using Lantern.Domain;

namespace Lantern.Application;

public sealed record NetworkScanUpdate(
    NetworkScanUpdateType UpdateType,
    DiscoveredDevice? Device,
    string Message,
    DateTimeOffset TimestampUtc,
    int? Completed = null,
    int? Total = null,
    NetworkDevice? InventoryDevice = null,
    NetworkEvent? Event = null)
{
    public static NetworkScanUpdate Progress(string message, int? completed = null, int? total = null)
        => new(NetworkScanUpdateType.ScanProgress, null, message, DateTimeOffset.UtcNow, completed, total);
}
