using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Domain;

namespace Lantern.Infrastructure.NetworkScanning.Classification;

public sealed class DeviceClassificationProvider : IDeviceClassificationProvider
{
    public DeviceClassification Classify(DiscoveredDevice device)
    {
        var hostname = device.Hostname?.ToLowerInvariant() ?? string.Empty;
        var vendor = device.Vendor?.ToLowerInvariant() ?? string.Empty;
        var mac = device.MacAddress?.ToLowerInvariant() ?? string.Empty;
        var ports = device.OpenPorts.Select(port => port.Port).ToHashSet();

        if (vendor.Contains("espressif") || mac.StartsWith("24:6f:28") || mac.StartsWith("30:ae:a4") || hostname.StartsWith("esp_") || hostname.StartsWith("esp-"))
        {
            return new(DeviceType.Esp32Device, "Likely ESP32 device. Espressif vendor, MAC prefix, or ESP-style hostname was detected.", 0.9);
        }

        if (ports.Contains(8006) || hostname.Contains("proxmox") || hostname.Contains("pve"))
        {
            return new(DeviceType.LinuxServer, "Likely Proxmox server. The Proxmox web console port or a Proxmox-style hostname was observed.", 0.92);
        }

        if (device.OpenPorts.Any(p => p.Port == 9443 && p.ServiceName == "Portainer UI"))
        {
            return new(DeviceType.DockerServer, "Docker server detected. A Portainer web interface answered on https://device:9443/#!/auth.", 0.95);
        }

        if (ports.Contains(8096))
        {
            return new(DeviceType.JellyfinServer, "Jellyfin server detected. The Jellyfin service port 8096 is available on this device.", 0.95);
        }

        if (ports.Contains(22) && (ports.Contains(80) || ports.Contains(443) || ports.Contains(8006) || ports.Contains(8080) || ports.Contains(9090)))
        {
            return new(DeviceType.LinuxServer, "Likely server. SSH and one or more web/admin ports were observed.", 0.78);
        }

        var hasPrinterVendor = vendor.Contains("brother") || vendor.Contains("canon") || vendor.Contains("epson") || vendor.Contains("ricoh") || vendor.Contains("kyocera") || vendor == "hp";
        var hasPrinterName = hostname.Contains("printer") || hostname.Contains("print") || hostname.Contains("laserjet") || hostname.Contains("officejet");
        if (hasPrinterVendor || hasPrinterName || ports.Contains(9100) && !ports.Contains(22) && !ports.Contains(8006) && (ports.Contains(80) || ports.Contains(443)))
        {
            return new(DeviceType.Printer, "Likely printer. Printer vendor, hostname, or direct-printing port was observed.", 0.86);
        }

        if (ports.Contains(3389) || hostname.StartsWith("desktop-") || hostname.StartsWith("laptop-") || hostname.Contains("windows"))
        {
            return new(DeviceType.WindowsPc, "Likely Windows PC. The hostname or Remote Desktop service looks like a Windows computer.", 0.82);
        }

        if (ports.Contains(22) && (hostname.Contains("server") || hostname.Contains("nas") || vendor.Contains("raspberry") || vendor.Contains("qnap") || vendor.Contains("synology")))
        {
            return new(DeviceType.LinuxServer, "Likely Linux server or NAS. SSH is available and the hostname or vendor looks server-like.", 0.8);
        }

        if (vendor.Contains("samsung") || vendor.Contains("lg ") || vendor.Contains("roku") || ports.Contains(8009) || ports.Contains(554))
        {
            return new(DeviceType.SmartTv, "Likely Smart TV. Media vendor or media-control ports were observed.", 0.76);
        }

        if (vendor.Contains("nintendo") || vendor.Contains("sony interactive") || vendor.Contains("microsoft") && hostname.Contains("xbox"))
        {
            return new(DeviceType.GamingConsole, "Likely gaming console. The vendor or hostname matches a common console pattern.", 0.78);
        }

        if (vendor.Contains("apple") || hostname.Contains("iphone") || hostname.Contains("android") || vendor.Contains("samsung"))
        {
            return new(DeviceType.Smartphone, "Likely smartphone or tablet. The hostname or vendor matches common mobile devices.", 0.7);
        }

        if (device.IpAddress.EndsWith(".1", StringComparison.Ordinal) || hostname.Contains("router") || hostname.Contains("gateway") || vendor.Contains("mikrotik") || vendor.Contains("ubiquiti") || vendor.Contains("tp-link"))
        {
            return new(DeviceType.Router, "Likely router or gateway. The address, hostname, or vendor looks like network equipment.", 0.74);
        }

        if (ports.Contains(1883) || ports.Contains(8123) || vendor.Contains("tuya") || vendor.Contains("sonoff"))
        {
            return new(DeviceType.IotDevice, "Likely IoT device. Smart-home vendor or automation-related ports were observed.", 0.66);
        }

        return new(DeviceType.UnknownDevice, "LANtern found the device but does not have enough clues to identify it yet.", 0.2);
    }
}
