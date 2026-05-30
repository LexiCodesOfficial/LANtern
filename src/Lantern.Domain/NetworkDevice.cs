namespace Lantern.Domain;

public sealed class NetworkDevice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FriendlyName { get; set; } = "Unknown Device";
    public string? Hostname { get; set; }
    public HostnameSource HostnameSource { get; set; }
    public string? MacAddress { get; set; }
    public string? Vendor { get; set; }
    public DeviceType DeviceType { get; set; } = DeviceType.UnknownDevice;
    public string Notes { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;
    public string? LastIpAddress { get; set; }
    public string ClassificationExplanation { get; set; } = "LANtern has not seen enough clues to identify this device yet.";
    public bool IsUserNamed { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? Hostname ?? "Unknown Device" : FriendlyName;
}
