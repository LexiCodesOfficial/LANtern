using System.Text.Json;
using Lantern.Application;
using Lantern.Application.Abstractions;

namespace Lantern.Infrastructure.Persistence;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public JsonAppSettingsStore()
    {
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LANtern",
            "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var temporaryPath = $"{_settingsPath}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, options, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _settingsPath, true);
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
