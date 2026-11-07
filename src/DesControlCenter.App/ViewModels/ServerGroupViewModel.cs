using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DesControlCenter.Core.Enums;

namespace DesControlCenter.App.ViewModels;

/// <summary>
/// One VPS card plus every resource that lives on it (services, VPN tunnels, sites) — drives the
/// "VPS → its resources" hierarchy in the dashboard. Children is deliberately object-typed: the
/// view picks a DataTemplate per concrete row-VM type, so this stays a flat list instead of a
/// parallel type hierarchy.
/// The card is collapsed by default: <see cref="SummaryText"/> and <see cref="Health"/> keep the
/// whole server readable at a glance, and <see cref="IsExpanded"/> opens the per-resource rows.
/// </summary>
public sealed partial class ServerGroupViewModel : ObservableObject
{
    private static readonly HashSet<string> ChildHealthProperties = new() { "Health", "State", "IsOnline", "HasBeenChecked", "IsPending" };

    public ServerRowViewModel Server { get; }
    public ObservableCollection<object> Children { get; } = new();

    [ObservableProperty] private bool isExpanded;
    [ObservableProperty] private int okCount;
    [ObservableProperty] private int warningCount;
    [ObservableProperty] private int problemCount;
    [ObservableProperty] private int pendingCount;

    public ServerGroupViewModel(ServerRowViewModel server)
    {
        Server = server;
        Children.CollectionChanged += OnChildrenChanged;
        Server.PropertyChanged += OnServerChanged;
    }

    /// <summary>"8 сервисов" — the counts next to it are drawn as coloured dots in the card (colour emoji do not render in WPF text).</summary>
    public string TotalLabel => Counted == 0 ? "ресурсов нет" : $"{Counted} {Plural(Counted)}";

    /// <summary>Switches are controls, not services: they are shown on the card but not counted in the summary.</summary>
    private int Counted => Children.Count(c => c is not SwitchRowViewModel);

    /// <summary>The same summary as plain text (tooltips, tests).</summary>
    public string SummaryText
    {
        get
        {
            var total = Counted;
            if (total == 0)
            {
                return "ресурсов нет";
            }

            var text = $"{total} {Plural(total)} · {OkCount} в норме";
            if (WarningCount > 0)
            {
                text += $" · {WarningCount} внимание";
            }

            return text + $" · {ProblemCount} проблем";
        }
    }

    /// <summary>Worst of: server unreachable, any resource not fine, housekeeping note. Real state, silencing ignored
    /// (a silenced problem still shows on the card, it just doesn't ring the tray).</summary>
    public HealthLevel Health
    {
        get
        {
            if ((Server.HasBeenChecked && !Server.IsOnline) || ProblemCount > 0)
            {
                return HealthLevel.Problem;
            }

            return WarningCount > 0 || !string.IsNullOrEmpty(Server.MaintenanceNote) ? HealthLevel.Warning : HealthLevel.Ok;
        }
    }

    public static bool IsPending(object child) => child switch
    {
        ServiceRowViewModel s => s.IsPending,
        VpnRowViewModel v => !v.HasBeenChecked,
        WebsiteRowViewModel w => !w.HasBeenChecked,
        _ => false
    };

    public static HealthLevel ChildHealth(object child) => child switch
    {
        ServiceRowViewModel s => s.Health,
        VpnRowViewModel v => v.Health,
        WebsiteRowViewModel w => w.Health,
        _ => HealthLevel.Ok
    };

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<INotifyPropertyChanged>())
            {
                item.PropertyChanged -= OnChildChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<INotifyPropertyChanged>())
            {
                item.PropertyChanged += OnChildChanged;
            }
        }

        Recompute();
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && ChildHealthProperties.Contains(name))
        {
            Recompute();
        }
    }

    private void OnServerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ServerRowViewModel.IsOnline) or nameof(ServerRowViewModel.HasBeenChecked) or nameof(ServerRowViewModel.MaintenanceNote))
        {
            OnPropertyChanged(nameof(Health));
        }
    }

    private void Recompute()
    {
        var ok = 0;
        var warning = 0;
        var problem = 0;
        var pending = 0;
        foreach (var child in Children.Where(c => c is not SwitchRowViewModel))
        {
            if (IsPending(child))
            {
                pending++;
                continue;
            }

            switch (ChildHealth(child))
            {
                case HealthLevel.Problem: problem++; break;
                case HealthLevel.Warning: warning++; break;
                default: ok++; break;
            }
        }

        OkCount = ok;
        WarningCount = warning;
        ProblemCount = problem;
        PendingCount = pending;
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(TotalLabel));
        OnPropertyChanged(nameof(Health));
    }

    private static string Plural(int n) => (n % 100) switch
    {
        >= 11 and <= 14 => "сервисов",
        _ => (n % 10) switch { 1 => "сервис", >= 2 and <= 4 => "сервиса", _ => "сервисов" }
    };
}
