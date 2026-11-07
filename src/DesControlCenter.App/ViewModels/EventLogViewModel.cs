using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;

namespace DesControlCenter.App.ViewModels;

public partial class EventLogViewModel : ObservableObject
{
    private readonly IEventStore _eventStore;
    private readonly IConfigStore _configStore;

    public ObservableCollection<EventLogEntry> Entries { get; } = new();
    public ObservableCollection<ProjectOption> ProjectOptions { get; } = new();
    public ObservableCollection<string> ResourceKindOptions { get; } = new() { "Все", "Server", "Website", "Service", "Vpn" };
    public ObservableCollection<string> EventKindOptions { get; } = new() { "Все", "StatusChanged", "UserAction", "CheckError" };

    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private string selectedResourceKind = "Все";
    [ObservableProperty] private string selectedEventKind = "Все";
    [ObservableProperty] private ProjectOption? selectedProject;
    [ObservableProperty] private string searchText = string.Empty;

    public EventLogViewModel(IEventStore eventStore, IConfigStore configStore)
    {
        _eventStore = eventStore;
        _configStore = configStore;
    }

    public async Task InitializeAsync()
    {
        var config = await _configStore.LoadAsync();

        ProjectOptions.Clear();
        ProjectOptions.Add(new ProjectOption(null, "Все проекты"));
        foreach (var project in config.Projects)
        {
            ProjectOptions.Add(new ProjectOption(project.Id, project.Name));
        }

        SelectedProject = ProjectOptions[0];

        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var filter = new EventLogFilter
        {
            FromUtc = FromDate?.ToUniversalTime(),
            ToUtc = ToDate?.AddDays(1).ToUniversalTime(),
            ProjectId = SelectedProject?.Id,
            ResourceKind = SelectedResourceKind == "Все" ? null : Enum.Parse<ResourceKind>(SelectedResourceKind),
            EventKind = SelectedEventKind == "Все" ? null : Enum.Parse<EventKind>(SelectedEventKind),
            Limit = 500
        };

        var results = await _eventStore.QueryAsync(filter);

        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? results
            : results.Where(r => r.ResourceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        Entries.Clear();
        foreach (var entry in filtered)
        {
            Entries.Add(entry);
        }
    }
}
