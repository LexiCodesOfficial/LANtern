namespace Lantern.Application.Abstractions;

public interface IDeviceClassificationProvider
{
    DeviceClassification Classify(DiscoveredDevice device);
}
