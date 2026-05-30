namespace Lantern.Application;

public enum NetworkScanUpdateType
{
    ScanStarted,
    KnownDeviceLoaded,
    DeviceDiscovered,
    DeviceEnriched,
    DeviceClassified,
    DeviceUpdated,
    ScanProgress,
    ScanCompleted,
    ScanFailed
}
