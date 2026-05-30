using Lantern.Domain;

namespace Lantern.Application.Abstractions;

public interface INotificationService
{
    bool IsEnabled { get; set; }
    Task NotifyAsync(NetworkEvent networkEvent, CancellationToken cancellationToken = default);
}
