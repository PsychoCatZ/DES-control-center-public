using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Win32;
using DesControlCenter.Core.Abstractions;

namespace DesControlCenter.Infrastructure.Apps;

/// <summary>
/// See <see cref="IDesktopAppLauncher"/>. Two detection paths, both checked against the real
/// machine at call time rather than a hardcoded assumption:
///
/// 1. A custom URI scheme (e.g. "tg", "whatsapp", "assistant") — used only if HKEY_CLASSES_ROOT
///    actually has that key registered as a URL protocol. Deliberately NOT used for a
///    multi-server app like Mattermost: its protocol handler always routes through an "Add
///    server" prompt even when that server is already configured, which is a worse experience
///    than just opening the app plainly — use the app-name path for those instead.
/// 2. A Start Menu app name (for apps with no protocol, or where the protocol shouldn't be used —
///    e.g. "ChatGPT", "Gemini", "Mattermost", and Chrome-hosted PWA shortcuts like "DeepSeek"/
///    "Qwen") — resolved via "Get-StartApps", the same mechanism Windows' own Start Menu search
///    uses, by name rather than a fixed app id: a packaged app's id can carry a
///    version/publisher-specific suffix that changes on reinstall, but its display name doesn't.
///    An absolute path to an existing .exe in the same field is launched directly instead.
/// </summary>
public sealed class WindowsDesktopAppLauncher : IDesktopAppLauncher
{
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(15);

    // Resolving via PowerShell takes about a second; a resolved app id doesn't change within a
    // single run of DES Control Center, so only pay that cost once. A FAILED lookup is not cached:
    // right after Windows login (or under load) Get-StartApps can be slow or time out, and caching
    // that as "no such app" made ChatGPT/Gemini open in the browser until DES was restarted.
    private readonly ConcurrentDictionary<string, string> _resolvedAppIds = new();

    public async Task OpenAsync(string? desktopProtocol, string? desktopAppName, string url, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(desktopProtocol) && IsProtocolRegistered(desktopProtocol))
        {
            Process.Start(new ProcessStartInfo($"{desktopProtocol}://") { UseShellExecute = true });
            return;
        }

        if (!string.IsNullOrWhiteSpace(desktopAppName))
        {
            // An explicit path to an installed .exe (apps that put no Start Menu shortcut, e.g. the
            // owner's own custom desktop application builds under %LocalAppData%) — launch it directly.
            if (Path.IsPathRooted(desktopAppName) && File.Exists(desktopAppName))
            {
                Process.Start(new ProcessStartInfo(desktopAppName)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(desktopAppName)
                });
                return;
            }

            var appId = await ResolveAppIdCachedAsync(desktopAppName, cancellationToken);
            if (appId is not null && TryLaunchByAppId(appId))
            {
                return;
            }
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task<string?> ResolveAppIdCachedAsync(string appName, CancellationToken cancellationToken)
    {
        if (_resolvedAppIds.TryGetValue(appName, out var cached) && cached is not null)
        {
            return cached;
        }

        var resolved = await ResolveAppIdAsync(appName, cancellationToken);
        if (resolved is not null)
        {
            _resolvedAppIds[appName] = resolved;
        }

        return resolved;
    }

    private static bool IsProtocolRegistered(string scheme)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(scheme);
        return key?.GetValue("URL Protocol") is not null;
    }

    private static async Task<string?> ResolveAppIdAsync(string appName, CancellationToken cancellationToken)
    {
        var escaped = appName.Replace("'", "''");
        // Windows PowerShell 5.1 writes redirected stdout in the console's OEM codepage by
        // default (cp866 on a Russian system, not UTF-8) — an AppID under a Cyrillic user
        // profile path (e.g. C:\Users\ExampleUser\...) comes back mojibake'd unless both sides are told
        // to use UTF-8 explicitly.
        var script = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; "
            // Prefer the packaged desktop app when a Chrome PWA has the same display name.
            + "(Get-StartApps | Where-Object { $_.Name -eq '" + escaped + "' } "
            + "| Sort-Object @{Expression={ if ($_.AppID -match '^[^!]+![^!]+$') { 0 } elseif ($_.AppID -like 'Chrome.*') { 2 } else { 1 } }}, AppID "
            + "| Select-Object -First 1 -ExpandProperty AppID)";

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ResolveTimeout);

            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            var trimmed = output.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Get-StartApps reports a plain Win32 shortcut with no set AppUserModelID as its raw
    /// target .exe path (needs a direct launch) — everything else it returns (packaged apps like
    /// "Publisher.App_hash!App", and Chrome-hosted PWA/app shortcuts like "Chrome._crx_&lt;id&gt;")
    /// is launchable through the shell's own AppsFolder, the same virtual folder Start Menu
    /// search uses, so it isn't limited to one specific id shape.</summary>
    private static bool TryLaunchByAppId(string appId)
    {
        try
        {
            if (File.Exists(appId))
            {
                Process.Start(new ProcessStartInfo(appId) { UseShellExecute = true });
                return true;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
