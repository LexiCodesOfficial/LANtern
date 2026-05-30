namespace Lantern.Domain;

public enum NetworkEventKind
{
    NewDeviceJoined,
    DeviceDisappeared,
    DeviceReturnedOnline,
    DeviceChangedIp,
    DeviceChangedHostname,
    DeviceChangedStatus,
    DeviceRenamed
}
