using Lantern.Application.Abstractions;
using Lantern.Domain;

namespace Lantern.Infrastructure.Notifications;

public sealed class InMemoryNotificationService : INotificationService
{
    public bool IsEnabled { get; set; } = true;
    public List<NetworkEvent> Notifications { get; } = [];

    public Task NotifyAsync(NetworkEvent networkEvent, CancellationToken cancellationToken = default)
    {
        if (IsEnabled)
        {
            Notifications.Add(networkEvent);
        }

        return Task.CompletedTask;
    }
}
