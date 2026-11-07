using System.Diagnostics;
using Microsoft.Win32;

namespace DesControlCenter.App.Startup;

/// <summary>Toggles a per-user "start with Windows" entry via the standard Run registry key (no admin rights needed).</summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesControlCenter";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (exePath is not null)
        {
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
    }
}
