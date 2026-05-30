using Lantern.Domain;

namespace Lantern.Application;

public sealed record DeviceClassification(DeviceType DeviceType, string Explanation, double Confidence);
