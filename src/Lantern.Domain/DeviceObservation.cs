namespace Lantern.Domain;

public sealed record DeviceObservation(
    string IpAddress,
    string? MacAddress,
    string? Hostname,
    string? Vendor,
    IReadOnlyCollection<int> OpenPorts,
    DateTimeOffset SeenUtc);
