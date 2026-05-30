namespace Lantern.Infrastructure.Integrations.MikroTik;

public sealed class MikroTikSettingsStore
{
    public MikroTikSettings Current { get; } = new();
}
