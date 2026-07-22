using System.Text.Json;
using HomeKTV.Core.Configuration;
using HomeKTV.Core.Portable;

namespace HomeKTV.Infrastructure.Configuration;

public sealed class JsonSettingsStore(PortablePaths paths)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public async Task<(HomeKtvSettings Settings, string? RecoveryMessage)> LoadAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        if (!File.Exists(paths.Settings))
        {
            var defaults = new HomeKtvSettings();
            await SaveAsync(defaults, cancellationToken);
            return (defaults, null);
        }
        try
        {
            await using var stream = File.OpenRead(paths.Settings);
            return (HomeKtvSettingsValidator.Normalize(await JsonSerializer.DeserializeAsync<HomeKtvSettings>(stream, Options, cancellationToken) ?? new HomeKtvSettings()), null);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            var damaged = Path.Combine(paths.Data, $"Settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(paths.Settings, damaged, true);
            var defaults = new HomeKtvSettings();
            await SaveAsync(defaults, cancellationToken);
            return (defaults, $"设置文件损坏，已保留为 {Path.GetFileName(damaged)} 并恢复默认设置。");
        }
    }

    public async Task SaveAsync(HomeKtvSettings settings, CancellationToken cancellationToken = default)
    {
        HomeKtvSettingsValidator.Normalize(settings);
        paths.EnsureDirectories();
        var temporary = paths.Settings + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
        File.Move(temporary, paths.Settings, true);
    }
}
