namespace DesControlCenter.Infrastructure.Ssh;

/// <summary>Finds the system's OpenSSH client, preferring the Windows-shipped copy over whatever else is on PATH.</summary>
public static class SshExecutableLocator
{
    public static string Resolve()
    {
        var windowsDir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        var builtIn = Path.Combine(windowsDir, "System32", "OpenSSH", "ssh.exe");
        return File.Exists(builtIn) ? builtIn : "ssh";
    }
}
