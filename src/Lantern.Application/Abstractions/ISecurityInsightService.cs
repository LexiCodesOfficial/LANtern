using Lantern.Domain;

namespace Lantern.Application.Abstractions;

public interface ISecurityInsightService
{
    IReadOnlyList<SecurityInsight> BuildInsights(NetworkDevice device, IReadOnlyCollection<int> openPorts);
}
