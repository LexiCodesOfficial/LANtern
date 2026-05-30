namespace Lantern.Infrastructure.NetworkScanning;

public static class NetworkPortNames
{
    public static string GetServiceName(int port) => port switch
    {
        22 => "SSH",
        80 => "HTTP",
        443 => "HTTPS",
        445 => "SMB",
        554 => "RTSP",
        1883 => "MQTT",
        3000 => "Dev Server",
        3389 => "Remote Desktop",
        5000 => "Dev Server",
        8006 => "Proxmox",
        8009 => "Media Control",
        8080 => "HTTP Alternate",
        8096 => "Jellyfin",
        9443 => "Portainer UI",
        8123 => "Home Assistant",
        9090 => "Prometheus",
        9100 => "Direct Print",
        _ => $"Port {port}"
    };
}
