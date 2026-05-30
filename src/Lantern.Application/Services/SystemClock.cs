using Lantern.Application.Abstractions;

namespace Lantern.Application.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
