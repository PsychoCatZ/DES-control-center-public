using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesControlCenter.App.Services;
using DesControlCenter.App.Startup;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Models;

namespace DesControlCenter.App.ViewModels;

/// <summary>
/// CRUD over the whole configuration (projects/servers/websites/services) plus app-level
/// settings. "Save" is the only thing that writes to disk and restarts polling to match.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigStore _configStore;
    private readonly MainViewModel _mainViewModel;
    private readonly IDialogService _dialogService;

    private AppConfig _config = new();

    public ObservableCollection<Project> Projects { get; } = new();
    public ObservableCollection<ServerResource> Servers { get; } = new();
    public ObservableCollection<Website> Websites { get; } = new();
    public ObservableCollection<ServiceResource> Services { get; } = new();
    public ObservableCollection<VpnResource> Vpns { get; } = new();

    [ObservableProperty] private Project? selectedProject;
    [ObservableProperty] private ServerResource? selectedServer;
    [ObservableProperty] private Website? selectedWebsite;
    [ObservableProperty] private ServiceResource? selectedService;
    [ObservableProperty] private VpnResource? selectedVpn;

    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool minimizeToTrayOnClose;
    [ObservableProperty] private bool showNotifications;

    public SettingsViewModel(IConfigStore configStore, MainViewModel mainViewModel, IDialogService dialogService)
    {
        _configStore = configStore;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
    }

    public async Task InitializeAsync()
    {
        _config = await _configStore.LoadAsync();
        Reload();
    }

    private void Reload()
    {
        Projects.Clear();
        foreach (var project in _config.Projects)
        {
            Projects.Add(project);
        }

        Servers.Clear();
        foreach (var server in _config.Servers)
        {
            Servers.Add(server);
        }

        Websites.Clear();
        foreach (var website in _config.Websites)
        {
            Websites.Add(website);
        }

        Services.Clear();
        foreach (var service in _config.Services)
        {
            Services.Add(service);
        }

        Vpns.Clear();
        foreach (var vpn in _config.Vpns)
        {
            Vpns.Add(vpn);
        }

        StartWithWindows = _config.Settings.StartWithWindows;
        MinimizeToTrayOnClose = _config.Settings.MinimizeToTrayOnClose;
        ShowNotifications = _config.Settings.ShowNotifications;
    }

    [RelayCommand]
    private void AddProject()
    {
        var project = new Project { Name = "Новый проект" };
        Projects.Add(project);
        SelectedProject = project;
    }

    [RelayCommand]
    private void RemoveProject()
    {
        if (SelectedProject is null)
        {
            return;
        }

        Projects.Remove(SelectedProject);
        SelectedProject = null;
    }

    [RelayCommand]
    private void AddServer()
    {
        var server = new ServerResource { Name = "Новый сервер", SshUsername = "root" };
        Servers.Add(server);
        SelectedServer = server;
    }

    [RelayCommand]
    private void RemoveServer()
    {
        if (SelectedServer is null)
        {
            return;
        }

        Servers.Remove(SelectedServer);
        SelectedServer = null;
    }

    [RelayCommand]
    private void AddWebsite()
    {
        var website = new Website { Name = "Новый сайт", Url = "https://" };
        Websites.Add(website);
        SelectedWebsite = website;
    }

    [RelayCommand]
    private void RemoveWebsite()
    {
        if (SelectedWebsite is null)
        {
            return;
        }

        Websites.Remove(SelectedWebsite);
        SelectedWebsite = null;
    }

    [RelayCommand]
    private void AddService()
    {
        var service = new ServiceResource
        {
            DisplayName = "Новый сервис",
            UnitName = "example.service",
            ServerId = Servers.FirstOrDefault()?.Id ?? Guid.Empty
        };
        Services.Add(service);
        SelectedService = service;
    }

    [RelayCommand]
    private void RemoveService()
    {
        if (SelectedService is null)
        {
            return;
        }

        Services.Remove(SelectedService);
        SelectedService = null;
    }

    [RelayCommand]
    private void AddVpn()
    {
        var vpn = new VpnResource
        {
            DisplayName = "Новый VPN",
            ServerId = Servers.FirstOrDefault()?.Id ?? Guid.Empty
        };
        Vpns.Add(vpn);
        SelectedVpn = vpn;
    }

    [RelayCommand]
    private void RemoveVpn()
    {
        if (SelectedVpn is null)
        {
            return;
        }

        Vpns.Remove(SelectedVpn);
        SelectedVpn = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _config.Projects = Projects.ToList();
        _config.Servers = Servers.ToList();
        _config.Websites = Websites.ToList();
        _config.Services = Services.ToList();
        _config.Vpns = Vpns.ToList();
        _config.Settings.StartWithWindows = StartWithWindows;
        _config.Settings.MinimizeToTrayOnClose = MinimizeToTrayOnClose;
        _config.Settings.ShowNotifications = ShowNotifications;

        await _configStore.SaveAsync(_config);
        AutoStartManager.SetEnabled(StartWithWindows);

        await _mainViewModel.ReloadConfigAsync();

        _dialogService.ShowInfo("Настройки", "Изменения сохранены и применены.");
    }
}
