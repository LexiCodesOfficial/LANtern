using Lantern.Domain;

namespace Lantern.Desktop.ViewModels;

public sealed class TimelineItemViewModel
{
    public TimelineItemViewModel(NetworkEvent networkEvent)
    {
        Id = networkEvent.Id;
        Title = networkEvent.Title;
        Description = networkEvent.Description;
        TimeText = networkEvent.OccurredUtc.ToLocalTime().ToString("g");
        Kind = networkEvent.Kind.ToString();
    }

    public Guid Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string TimeText { get; }
    public string Kind { get; }
}
