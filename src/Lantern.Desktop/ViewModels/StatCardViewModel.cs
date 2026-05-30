using Avalonia.Media;

namespace Lantern.Desktop.ViewModels;

public sealed record StatCardViewModel(string Label, string Value, IBrush Accent)
{
    public StatCardViewModel(string label, string value, string accent)
        : this(label, value, SolidColorBrush.Parse(accent))
    {
    }
}
