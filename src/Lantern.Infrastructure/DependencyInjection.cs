using Lantern.Application.Abstractions;
using Lantern.Infrastructure.Integrations.MikroTik;
using Lantern.Infrastructure.Integrations;
using Lantern.Infrastructure.Companion;
using Lantern.Infrastructure.NetworkScanning;
using Lantern.Infrastructure.NetworkScanning.Classification;
using Lantern.Infrastructure.NetworkScanning.Discovery;
using Lantern.Infrastructure.NetworkScanning.Enrichment;
using Lantern.Infrastructure.Networking;
using Lantern.Infrastructure.Notifications;
using Lantern.Infrastructure.Persistence;
using Lantern.Infrastructure.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLanternInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceRepository>(_ =>
        {
            var databasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LANtern",
                "lantern.db");

            return new SqliteDeviceRepository(databasePath);
        });

        services.AddSingleton<SeedVendorLookupService>();
        services.AddSingleton<IVendorLookupService>(provider => provider.GetRequiredService<SeedVendorLookupService>());
        services.AddSingleton<IVendorDatabaseService>(provider => provider.GetRequiredService<SeedVendorLookupService>());
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<MikroTikSettingsStore>();
        services.AddSingleton<IDhcpLeaseProvider, MikroTikDhcpLeaseProvider>();
        services.AddSingleton<IDiscoveryProvider, ArpDiscoveryProvider>();
        services.AddSingleton<IDiscoveryProvider, PingDiscoveryProvider>();
        services.AddSingleton<IDiscoveryProvider, TcpProbeDiscoveryProvider>();
        services.AddSingleton<IDiscoveryProvider, SsdpDiscoveryProvider>();
        services.AddSingleton<IDiscoveryProvider, MdnsServiceDiscoveryProvider>();
        services.AddSingleton<IEnrichmentProvider, MacAddressEnrichmentProvider>();
        services.AddSingleton<IEnrichmentProvider, DhcpLeaseHostnameEnrichmentProvider>();
        services.AddSingleton<IEnrichmentProvider, VendorEnrichmentProvider>();
        services.AddSingleton<IEnrichmentProvider, HostnameEnrichmentProvider>();
        services.AddSingleton<IEnrichmentProvider, PortProbeEnrichmentProvider>();
        services.AddSingleton<IDeviceClassificationProvider, DeviceClassificationProvider>();
        services.AddSingleton<INetworkScanPipeline, NetworkScanPipeline>();
        services.AddSingleton<INetworkScanner, LocalNetworkScanner>();
        services.AddSingleton<INotificationService, DesktopNotificationService>();
        services.AddSingleton<IExportService, LocalExportService>();
        services.AddSingleton<ILocalIntegrationService, LocalIntegrationService>();
        services.AddSingleton<ICompanionDashboardService, CompanionDashboardService>();
        services.AddSingleton<ILocalSubnetService, LocalSubnetService>();

        return services;
    }
}
