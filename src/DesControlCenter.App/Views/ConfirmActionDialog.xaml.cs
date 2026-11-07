using System.Windows;
using DesControlCenter.App.Startup;

namespace DesControlCenter.App.Views;

public partial class ConfirmActionDialog : Window
{
    public string Message { get; }

    private ConfirmActionDialog(string title, string message)
    {
        Message = message;
        DataContext = this;
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
        Title = title;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public static bool Show(Window? owner, string title, string message)
    {
        var dialog = new ConfirmActionDialog(title, message);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true;
    }
}
