using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DesControlCenter.App.Startup;

/// <summary>
/// WPF never recolours the native title bar on its own — without this, every window shows a
/// light Windows 10/11 title bar/caption buttons above a dark client area (or the other way
/// round). Tracks every window it has seen so a theme switch updates all of them live.
/// </summary>
public static class WindowChromeHelper
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    private static readonly List<WeakReference<Window>> Windows = new();
    private static bool _dark = true;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    /// <summary>Registers the window and gives it the title bar matching the current theme.</summary>
    public static void EnableDarkTitleBar(Window window)
    {
        Windows.Add(new WeakReference<Window>(window));

        if (PresentationSource.FromVisual(window) is not null || window.IsLoaded)
        {
            Apply(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => Apply(window);
        }
    }

    /// <summary>Switches the title bar of every open window and of windows opened later.</summary>
    public static void SetDark(bool dark)
    {
        _dark = dark;
        Windows.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var reference in Windows.ToList())
        {
            if (reference.TryGetTarget(out var window))
            {
                Apply(window);
            }
        }
    }

    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var value = _dark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
    }
}
