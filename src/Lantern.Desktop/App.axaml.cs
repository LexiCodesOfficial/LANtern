using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lantern.Application.Abstractions;
using Lantern.Application.Services;
using Lantern.Desktop.ViewModels;
using Lantern.Desktop.Views;
using Lantern.Infrastructure;
using Lantern.Infrastructure.Integrations.MikroTik;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.Desktop;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (_, _) => _services?.Dispose();
            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDeviceClassifier, DeviceClassifier>();
        services.AddSingleton<ISecurityInsightService, SecurityInsightService>();
        services.AddSingleton<DeviceInventoryService>();
        services.AddLanternInfrastructure();
        services.AddTransient<MainWindowViewModel>();
    }
}
