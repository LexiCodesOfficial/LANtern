namespace Lantern.Infrastructure.Integrations.MikroTik;

public sealed class MikroTikSettings
{
    public bool IsEnabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 8728;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
