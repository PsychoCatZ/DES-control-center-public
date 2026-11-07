using System.Windows;
using System.Windows.Media;
using DesControlCenter.App.Startup;
using DesControlCenter.App.Themes;
using DesControlCenter.Infrastructure.Persistence;
using Microsoft.Win32;

namespace DesControlCenter.App.Services;

/// <summary>Dark / light / follow-Windows. Applying a theme recolours the shared brushes in place and
/// flips the native title bars, so open windows change immediately.</summary>
public sealed class ThemeService
{
    private readonly JsonUiSettingsStore _store;

    public ThemeService(JsonUiSettingsStore store)
    {
        _store = store;
        Mode = store.LoadTheme();
    }

    public ThemeMode Mode { get; private set; }

    /// <summary>Whether the light palette is in effect right now (resolves "follow Windows").</summary>
    public bool IsLight => Mode == ThemeMode.Light || (Mode == ThemeMode.System && WindowsUsesLightApps());

    public void Initialize()
    {
        Apply();

        // "Follow Windows" must react when the user flips Windows' own setting while DES is running.
        SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            if (Mode == ThemeMode.System)
            {
                Application.Current?.Dispatcher.BeginInvoke(Apply);
            }
        };
    }

    public void SetMode(ThemeMode mode)
    {
        Mode = mode;
        try
        {
            _store.SaveTheme(mode);
        }
        catch
        {
            // Not being able to remember the choice must not stop it from applying now.
        }

        Apply();
    }

    private void Apply()
    {
        var light = IsLight;
        var palette = light ? ThemePalettes.Light : ThemePalettes.Dark;
        var resources = Application.Current.Resources;

        foreach (var (key, color) in palette)
        {
            resources[key + "Color"] = color;

            // Everything refers to these brushes with DynamicResource, so replacing the entry recolours
            // all open windows at once.
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key + "Brush"] = brush;
        }

        WindowChromeHelper.SetDark(!light);
    }

    private static bool WindowsUsesLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }
}
