namespace Lantern.Domain;

public sealed record SecurityInsight(
    string Title,
    string Explanation,
    SecurityInsightSeverity Severity,
    int? Port = null);
