using Lantern.Application;

namespace Lantern.Desktop.ViewModels;

public sealed class IntegrationItemViewModel
{
    public IntegrationItemViewModel(IntegrationSummary summary)
    {
        Name = summary.Name;
        Status = summary.Status;
        Explanation = summary.Explanation;
        Address = summary.Address ?? string.Empty;
    }

    public string Name { get; }
    public string Status { get; }
    public string Explanation { get; }
    public string Address { get; }
}
