using Lantern.Domain;

namespace Lantern.Application;

public sealed record ScanSummary(
    int ObservedDevices,
    int NewDevices,
    int OnlineDevices,
    int OfflineDevices,
    IReadOnlyList<NetworkEvent> Events);
