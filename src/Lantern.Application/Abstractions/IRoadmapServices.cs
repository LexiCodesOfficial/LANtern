namespace Lantern.Application.Abstractions;

public interface IExportService
{
    Task<string> ExportDevicesCsvAsync(CancellationToken cancellationToken = default);
    Task<string> ExportTimelineCsvAsync(CancellationToken cancellationToken = default);
    Task<string> ExportPdfReportAsync(CancellationToken cancellationToken = default);
}

public interface ILocalIntegrationService
{
    Task<IReadOnlyList<IntegrationSummary>> InspectAsync(Guid deviceId, CancellationToken cancellationToken = default);
}

public interface ICompanionDashboardService
{
    bool IsRunning { get; }
    string Address { get; }
    Task StartAsync(int port, CancellationToken cancellationToken = default);
    Task StopAsync();
}

public interface ILocalSubnetService
{
    IReadOnlyList<string> GetPrivateSubnets();
}
