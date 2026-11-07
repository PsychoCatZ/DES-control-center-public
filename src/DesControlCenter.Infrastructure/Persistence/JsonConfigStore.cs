using System.Text.Json;
using System.Text.Json.Serialization;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;

namespace DesControlCenter.Infrastructure.Persistence;

/// <summary>
/// Round-trips the whole app configuration to %AppData%\DesControlCenter\app-settings.json. Only
/// ever stores connection coordinates (host/port/username/key path) — never key material or
/// passwords.
/// </summary>
public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _configPath;

    public JsonConfigStore(string? configPath = null)
    {
        // Deliberately not named "config.json": on at least one real machine, that exact
        // filename inside this folder was invisible (File.Exists/Directory.GetFiles) to THIS
        // app specifically while every external tool saw it fine, and a brand-new file under a
        // different name in the very same folder was visible immediately — so whatever the
        // mechanism, it keys off this file's name/path, not its content, folder, or encoding.
        _configPath = configPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesControlCenter", "app-settings.json");
    }

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_configPath))
        {
            return new AppConfig();
        }

        await using var stream = File.OpenRead(_configPath);
        var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, SerializerOptions, cancellationToken);
        return config ?? new AppConfig();
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(directory);

        // Write to a temp file and swap it in, so a crash mid-write can't corrupt the real config.
        var tempPath = _configPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, config, SerializerOptions, cancellationToken);
        }

        File.Move(tempPath, _configPath, overwrite: true);
    }
}
