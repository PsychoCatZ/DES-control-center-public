using System.IO;
using System.Text;
using System.Windows.Threading;
using DesControlCenter.App.ViewModels;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Monitoring;

namespace DesControlCenter.App.Services;

/// <summary>
/// Keeps three small files in the data folder (status.txt, status-line.txt, status.json) up to date, so another program — the owner's OSD
/// overlay — can show "DES: всё в порядке · серверов 8/8" without talking to DES. Written on every health change and once a minute
/// (so "Обновлено" proves DES is alive); on exit they say "DES не запущен". Files are replaced atomically; a reader that
/// holds a file open just misses one update.
/// </summary>
public sealed class StatusFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly MainViewModel _vm;
    private readonly string _dir;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };

    public StatusFileWriter(MainViewModel vm, string? directory = null)
    {
        _vm = vm;
        _dir = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesControlCenter");
    }

    public void Start()
    {
        _vm.OverallHealthChanged += (_, _) => Write(stopped: false);
        _timer.Tick += (_, _) => Write(stopped: false);
        _timer.Start();
        Write(stopped: false);
    }

    /// <summary>Called from App.OnExit.</summary>
    public void Stop()
    {
        _timer.Stop();
        Write(stopped: true);
    }

    private void Write(bool stopped)
    {
        try
        {
            var groups = _vm.ServerGroups;
            var snapshot = stopped
                ? StatusSnapshot.Stopped(DateTimeOffset.Now)
                : new StatusSnapshot(
                    _vm.OverallHealth,
                    _vm.IsLocalNetworkOutage,
                    _vm.Problems.Count,
                    _vm.SilencedProblemCount,
                    groups.Count(g => g.Server.HasBeenChecked && g.Server.IsOnline),
                    groups.Count,
                    groups.Count(g => !g.Server.HasBeenChecked),
                    _vm.Problems.ToList(),
                    DateTimeOffset.Now,
                    CollectWarnings());

            Directory.CreateDirectory(_dir);
            Replace("status.txt", StatusFormatter.Text(snapshot, stopped));
            Replace("status-line.txt", StatusFormatter.OneLine(snapshot, stopped));
            Replace("status.json", StatusFormatter.Json(snapshot, stopped));
        }
        catch (Exception)
        {
            // A status file is a convenience: never let a locked or missing folder disturb the dashboard.
        }
    }

    /// <summary>Rows that sit at "warning" (a stopped or unknown service, a certificate about to expire, a DNS hiccup): the reason behind a yellow status.</summary>
    private List<string> CollectWarnings()
    {
        var list = new List<string>();
        foreach (var group in _vm.ServerGroups)
        {
            foreach (var child in group.Children)
            {
                switch (child)
                {
                    case ServiceRowViewModel svc when svc.Health == HealthLevel.Warning && !svc.IsSilenced:
                        list.Add($"{svc.DisplayName}: не работает или состояние неизвестно");
                        break;
                    case WebsiteRowViewModel site when site.Health == HealthLevel.Warning && !site.IsSilenced:
                        list.Add($"{site.Name}: предупреждение");
                        break;
                }
            }
        }

        return list;
    }

    private void Replace(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, Utf8NoBom);
        File.Move(tmp, path, overwrite: true);
    }
}
