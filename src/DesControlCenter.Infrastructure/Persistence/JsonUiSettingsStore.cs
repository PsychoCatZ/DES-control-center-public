using System.Text.Json;

namespace DesControlCenter.Infrastructure.Persistence;

public enum ThemeMode
{
    Dark,
    Light,

    /// <summary>Follow the Windows "apps" light/dark setting, live.</summary>
    System
}

/// <summary>Purely visual preferences — <c>%AppData%\DesControlCenter\ui-settings.json</c>. Kept apart from
/// <c>app-settings.json</c> so the settings window, which rewrites that file whole, can never reset it.</summary>
public sealed class JsonUiSettingsStore
{
    private readonly string _path;

    public JsonUiSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesControlCenter", "ui-settings.json");
    }

    /// <summary>Dark (the original look) when nothing is saved or the file is unreadable.</summary>
    public ThemeMode LoadTheme()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return ThemeMode.Dark;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            return doc.RootElement.TryGetProperty("Theme", out var value)
                   && Enum.TryParse<ThemeMode>(value.GetString(), ignoreCase: true, out var mode)
                ? mode
                : ThemeMode.Dark;
        }
        catch
        {
            return ThemeMode.Dark;
        }
    }

    public void SaveTheme(ThemeMode mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(new { Theme = mode.ToString() }), new System.Text.UTF8Encoding(false));
    }
}
