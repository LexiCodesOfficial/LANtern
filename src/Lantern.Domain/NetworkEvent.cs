namespace Lantern.Domain;

public sealed record NetworkEvent(
    Guid Id,
    Guid? DeviceId,
    NetworkEventKind Kind,
    string Title,
    string Description,
    DateTimeOffset OccurredUtc);
