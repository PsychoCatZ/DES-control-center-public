using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;

namespace DesControlCenter.App.ViewModels;

public partial class LogViewerViewModel : ObservableObject
{
    private readonly IServiceLogReader _logReader;
    private ServerResource _server = new();
    private ServiceResource _service = new();

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string logText = string.Empty;
    [ObservableProperty] private bool isLoading;

    public LogViewerViewModel(IServiceLogReader logReader)
    {
        _logReader = logReader;
    }

    public async Task InitializeAsync(ServerResource server, ServiceResource service)
    {
        _server = server;
        _service = service;
        Title = $"Логи — {service.DisplayName} ({server.Name})";
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            LogText = await _logReader.GetRecentLogsAsync(_server, _service, lines: 200);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
