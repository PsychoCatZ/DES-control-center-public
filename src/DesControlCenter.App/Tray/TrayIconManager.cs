using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DesControlCenter.App.ViewModels;
using DesControlCenter.Core.Enums;
using H.NotifyIcon;

namespace DesControlCenter.App.Tray;

/// <summary>
/// Owns the tray icon and its context menu (spec §10). The icon reflects the overall health
/// computed by <see cref="MainViewModel"/>; the app is designed to live here when minimized.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private static readonly Uri OkIconUri = new("pack://application:,,,/Tray/Icons/ok.ico");
    private static readonly Uri WarningIconUri = new("pack://application:,,,/Tray/Icons/warning.ico");
    private static readonly Uri ProblemIconUri = new("pack://application:,,,/Tray/Icons/problem.ico");

    private readonly MainViewModel _mainViewModel;
    private TaskbarIcon? _taskbarIcon;
    private GlobalHotKey? _hotKey;

    public TrayIconManager(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    public void Initialize()
    {
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "DES Control Center",
            IconSource = new BitmapImage(OkIconUri),
            Visibility = Visibility.Visible
        };

        _taskbarIcon.TrayLeftMouseUp += (_, _) => ShowMainWindow();
        _taskbarIcon.ContextMenu = BuildContextMenu();

        // TaskbarIcon never gets attached to a visual tree in this app (no XAML host for it),
        // so its Win32 tray handle is never created without this explicit call — otherwise the
        // object exists and works in every other respect, it's just invisible in the tray.
        _taskbarIcon.ForceCreate();

        _mainViewModel.OverallHealthChanged += (_, _) => UpdateIcon();
        UpdateIcon();

        // Ctrl+Alt+C from anywhere in Windows: brings DES up from the tray or from behind other windows; pressed again while
        // DES is the window in front, it tucks it back into the tray.
        _hotKey = new GlobalHotKey(0x43, ToggleMainWindow);
        if (!_hotKey.IsRegistered)
        {
            System.Diagnostics.Debug.WriteLine("DES: Ctrl+Alt+C is already taken by another program; the shortcut is off.");
        }
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Открыть DES Control Center (Ctrl+Alt+C)", () =>
        {
            ShowMainWindow();
            return Task.CompletedTask;
        }));
        menu.Items.Add(CreateMenuItem("Проверить всё сейчас", () => _mainViewModel.CheckAllNowAsync()));
        menu.Items.Add(CreateMenuItem("Показать проблемы", () =>
        {
            ShowMainWindow();
            return Task.CompletedTask;
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Выход", () =>
        {
            Application.Current.Shutdown();
            return Task.CompletedTask;
        }));
        return menu;
    }

    private static MenuItem CreateMenuItem(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private static void ToggleMainWindow()
    {
        var window = Application.Current.MainWindow;
        if (window is not null && window.IsVisible && window.WindowState != WindowState.Minimized && window.IsActive)
        {
            window.Hide();
            return;
        }

        // From the shortcut the window comes up full screen (the owner's wish); from the tray icon it keeps its normal size.
        ShowMainWindow(maximize: true);
    }

    private static void ShowMainWindow(bool maximize = false)
    {
        var window = Application.Current.MainWindow;
        if (window is null)
        {
            return;
        }

        window.Show();
        window.WindowState = maximize ? WindowState.Maximized : WindowState.Normal;
        window.Activate();

        // A tray app is often behind other windows: Activate alone only flashes the taskbar button, so nudge it to the front.
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private void UpdateIcon()
    {
        if (_taskbarIcon is null)
        {
            return;
        }

        var uri = _mainViewModel.OverallHealth switch
        {
            HealthLevel.Problem => ProblemIconUri,
            HealthLevel.Warning => WarningIconUri,
            _ => OkIconUri
        };
        _taskbarIcon.IconSource = new BitmapImage(uri);

        if (_mainViewModel.IsLocalNetworkOutage)
        {
            _taskbarIcon.ToolTipText = "DES Control Center — нет связи с интернетом на этом компьютере";
            return;
        }

        var problems = _mainViewModel.Problems.Count;
        var silenced = _mainViewModel.SilencedProblemCount;
        _taskbarIcon.ToolTipText = problems == 0
            ? (_mainViewModel.OverallHealth == HealthLevel.Warning ? "DES Control Center — есть предупреждения" : "DES Control Center — всё в порядке")
            : $"DES Control Center — проблем: {problems}";
        if (silenced > 0)
        {
            _taskbarIcon.ToolTipText += $" (приглушено: {silenced})";
        }
    }

    public void Dispose()
    {
        _hotKey?.Dispose();
        _taskbarIcon?.Dispose();
    }
}
