using Lantern.Domain;

namespace Lantern.Application.Abstractions;

public interface IDeviceClassifier
{
    DeviceClassification Classify(DeviceObservation observation);
}
