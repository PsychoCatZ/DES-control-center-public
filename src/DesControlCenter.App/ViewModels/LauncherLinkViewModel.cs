using CommunityToolkit.Mvvm.Input;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;

namespace DesControlCenter.App.ViewModels;

/// <summary>A launcher card: title, a line saying what it is for, one "Открыть" button. No health, no polling.</summary>
public partial class LauncherLinkViewModel
{
    private readonly IDesktopAppLauncher _appLauncher;

    public LauncherLinkViewModel(LauncherLink source, IDesktopAppLauncher appLauncher)
    {
        Source = source;
        _appLauncher = appLauncher;
    }

    public LauncherLink Source { get; }
    public string Title => Source.Title;
    public string Subtitle => Source.Subtitle ?? string.Empty;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Source.Subtitle);

    [RelayCommand]
    private Task Open() => _appLauncher.OpenAsync(null, Source.DesktopAppName, Source.Url);
}
