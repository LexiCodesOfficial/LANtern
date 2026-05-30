namespace Lantern.Application;

public sealed record DhcpLeaseRecord(
    string IpAddress,
    string? MacAddress,
    string? Hostname,
    string Source);
