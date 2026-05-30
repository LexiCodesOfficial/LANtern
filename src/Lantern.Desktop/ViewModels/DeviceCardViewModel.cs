using Lantern.Domain;
using Avalonia.Media;

namespace Lantern.Desktop.ViewModels;

public sealed class DeviceCardViewModel
{
    public DeviceCardViewModel(NetworkDevice device)
    {
        Device = device;
    }

    public NetworkDevice Device { get; }
    public Guid Id => Device.Id;
    public string FriendlyName => Device.DisplayName;
    public string IpAddress => Device.LastIpAddress ?? "Not seen";
    public string MacAddress => Device.MacAddress ?? "Unknown";
    public string Hostname => Device.Hostname ?? "Unknown";
    public string HostnameLabel => Device.HostnameSource switch
    {
        HostnameSource.DhcpLease => "Active host name",
        HostnameSource.MulticastDns => "Local hostname",
        HostnameSource.Llmnr => "Local hostname",
        HostnameSource.ReverseDns => "DNS alias",
        _ => "Hostname"
    };
    public string Vendor => Device.Vendor ?? "Unknown";
    public string ClassificationExplanation => Device.ClassificationExplanation;
    public string LocationLabel => string.IsNullOrWhiteSpace(Device.LocationLabel) ? "Unlabeled" : Device.LocationLabel;
    public string DeviceTypeText => ToFriendlyType(Device.DeviceType);
    public string StatusText => Device.Status == DeviceStatus.Online ? "Online" : Device.Status == DeviceStatus.Offline ? "Offline" : "Unknown";
    public IBrush StatusBrush => SolidColorBrush.Parse(Device.Status == DeviceStatus.Online ? "#12B76A" : Device.Status == DeviceStatus.Offline ? "#98A2B3" : "#F79009");
    public IBrush AvatarBrush => SolidColorBrush.Parse(Device.DeviceType switch
    {
        DeviceType.Router => "#7A5AF8",
        DeviceType.WindowsPc => "#2E90FA",
        DeviceType.LinuxServer => "#344054",
        DeviceType.Smartphone => "#F04438",
        DeviceType.Printer => "#12B76A",
        DeviceType.SmartTv => "#06AED4",
        DeviceType.Esp32Device => "#F79009",
        DeviceType.IotDevice => "#16B364",
        DeviceType.GamingConsole => "#DD2590",
        DeviceType.JellyfinServer => "#7F56D9",
        DeviceType.DockerServer => "#2E90FA",
        _ => "#667085"
    });

    public string AvatarText => Device.DeviceType switch
    {
        DeviceType.Router => "RT",
        DeviceType.WindowsPc => "PC",
        DeviceType.LinuxServer => "SV",
        DeviceType.Smartphone => "PH",
        DeviceType.Printer => "PR",
        DeviceType.SmartTv => "TV",
        DeviceType.Esp32Device => "E3",
        DeviceType.IotDevice => "IO",
        DeviceType.GamingConsole => "GC",
        DeviceType.JellyfinServer => "JF",
        DeviceType.DockerServer => "DK",
        _ => "?"
    };

    public string Subtitle => Vendor == "Unknown"
        ? $"{DeviceTypeText} - {IpAddress}"
        : $"{Vendor} - {DeviceTypeText} - {IpAddress}";
    public string LastSeenText => $"Last seen {Device.LastSeenUtc.ToLocalTime():g}";
    public string FirstSeenText => Device.FirstSeenUtc.ToLocalTime().ToString("g");

    public static string ToFriendlyType(DeviceType type) => type switch
    {
        DeviceType.WindowsPc => "Windows PC",
        DeviceType.LinuxServer => "Linux Server",
        DeviceType.SmartTv => "Smart TV",
        DeviceType.Smartphone => "Smartphone",
        DeviceType.Printer => "Printer",
        DeviceType.Router => "Router",
        DeviceType.IotDevice => "IoT Device",
        DeviceType.Esp32Device => "ESP32 Device",
        DeviceType.GamingConsole => "Gaming Console",
        DeviceType.JellyfinServer => "Jellyfin Server",
        DeviceType.DockerServer => "Docker Server",
        _ => "Unknown Device"
    };
}
