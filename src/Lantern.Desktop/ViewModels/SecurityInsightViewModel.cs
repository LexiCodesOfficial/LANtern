using Lantern.Domain;

namespace Lantern.Desktop.ViewModels;

public sealed class SecurityInsightViewModel
{
    public SecurityInsightViewModel(SecurityInsight insight)
    {
        Title = insight.Port is null ? insight.Title : $"{insight.Title} Port {insight.Port}";
        Explanation = insight.Explanation;
        Severity = insight.Severity.ToString();
    }

    public string Title { get; }
    public string Explanation { get; }
    public string Severity { get; }
}
