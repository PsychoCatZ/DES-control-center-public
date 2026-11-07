using System.Windows;
using DesControlCenter.App.Views;

namespace DesControlCenter.App.Services;

public sealed class DialogService : IDialogService
{
    public bool Confirm(string title, string message) =>
        ConfirmActionDialog.Show(Application.Current.MainWindow, title, message);

    public void ShowInfo(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
