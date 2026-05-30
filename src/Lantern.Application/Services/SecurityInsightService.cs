using Lantern.Application.Abstractions;
using Lantern.Domain;

namespace Lantern.Application.Services;

public sealed class SecurityInsightService : ISecurityInsightService
{
    public IReadOnlyList<SecurityInsight> BuildInsights(NetworkDevice device, IReadOnlyCollection<int> openPorts)
    {
        var insights = new List<SecurityInsight>();

        AddIfOpen(22, "SSH is enabled on this device.", "This is commonly used for remote administration and is normal for servers, Raspberry Pi devices, and network appliances.", SecurityInsightSeverity.Info);
        AddIfOpen(3389, "Remote Desktop is available.", "This feature allows remote access to the computer. Only expose it to trusted networks.", SecurityInsightSeverity.Caution);
        AddIfOpen(445, "File sharing is available.", "This lets computers share files on the local network. It is common for Windows PCs and NAS devices.", SecurityInsightSeverity.Notice);
        AddIfOpen(80, "A web page is available.", "Many routers, printers, smart-home devices, and servers provide a local settings page.", SecurityInsightSeverity.Info);
        AddIfOpen(443, "A secure web page is available.", "This usually means the device has a local HTTPS settings page or service.", SecurityInsightSeverity.Info);
        AddIfOpen(9100, "Direct printing is available.", "This is common for printers and lets trusted computers print on the local network.", SecurityInsightSeverity.Info);
        AddIfOpen(1883, "MQTT is available.", "MQTT is often used by smart-home and maker devices to exchange messages locally.", SecurityInsightSeverity.Notice);
        AddIfOpen(8006, "Proxmox may be available.", "This is the usual web console port for Proxmox virtualization hosts.", SecurityInsightSeverity.Info);
        AddIfOpen(8096, "Jellyfin is available.", "This device is hosting a Jellyfin media service for your local network.", SecurityInsightSeverity.Info);
        AddIfOpen(8123, "Home Assistant may be available.", "This port is commonly used by Home Assistant dashboards on local networks.", SecurityInsightSeverity.Info);
        AddIfOpen(9443, "A Portainer page may be available.", "LANtern checks this secure web port for a Portainer sign-in page when identifying Docker servers.", SecurityInsightSeverity.Info);

        if (insights.Count == 0)
        {
            insights.Add(new("No common services found.", "LANtern did not find common local services during the last scan.", SecurityInsightSeverity.Info));
        }

        return insights;

        void AddIfOpen(int port, string title, string explanation, SecurityInsightSeverity severity)
        {
            if (openPorts.Contains(port))
            {
                insights.Add(new(title, explanation, severity, port));
            }
        }
    }
}
