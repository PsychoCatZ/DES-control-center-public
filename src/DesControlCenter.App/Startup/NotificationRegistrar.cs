using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DesControlCenter.App.Startup;

/// <summary>
/// Gives this unpackaged (non-MSIX) app a real Windows notification identity. Without this,
/// toast notifications from an unpackaged .NET app either fail to show at all on many Windows
/// versions, or show up attributed to a generic/wrong process instead of "DES Control Center".
/// Two pieces are required together: an explicit AppUserModelID on the process, and a Start Menu
/// shortcut carrying that same ID as a shell property (Windows resolves toast identity/branding
/// through the shortcut, not just the running process). Idempotent — cheap to call every launch.
/// </summary>
public static class NotificationRegistrar
{
    public const string AppUserModelId = "DES.ControlCenter";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    public static void Register()
    {
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        EnsureStartMenuShortcut();
    }

    private static void EnsureStartMenuShortcut()
    {
        var shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs\DES Control Center.lnk");

        if (File.Exists(shortcutPath))
        {
            return;
        }

        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (exePath is null)
        {
            return;
        }

        var shellLink = (IShellLinkW)new ShellLink();
        shellLink.SetPath(exePath);
        shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? "");
        shellLink.SetDescription("DES Control Center");

        var propertyStore = (IPropertyStore)shellLink;
        var key = PropertyKey.PKEY_AppUserModel_ID;
        var propVariant = PropVariant.FromString(AppUserModelId);
        try
        {
            propertyStore.SetValue(ref key, ref propVariant);
        }
        finally
        {
            propVariant.Dispose();
        }

        propertyStore.Commit();

        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        ((IPersistFile)shellLink).Save(shortcutPath, false);
    }
}
