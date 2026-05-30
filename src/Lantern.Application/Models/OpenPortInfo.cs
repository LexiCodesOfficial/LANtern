namespace Lantern.Application;

public sealed record OpenPortInfo(int Port, string ServiceName, DateTimeOffset ObservedUtc);
