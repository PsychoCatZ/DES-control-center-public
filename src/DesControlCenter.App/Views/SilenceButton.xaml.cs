using System.Windows;
using System.Windows.Controls;
using DesControlCenter.App.ViewModels;

namespace DesControlCenter.App.Views;

public partial class SilenceButton : UserControl
{
    public SilenceButton()
    {
        InitializeComponent();
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        // A ContextMenu lives outside the visual tree, so its commands can't inherit this row's
        // DataContext on their own — hand it over explicitly each time it opens.
        var menu = new ContextMenu
        {
            PlacementTarget = Trigger,
            DataContext = DataContext
        };

        void Add(string header, string commandName, bool visible = true)
        {
            if (!visible)
            {
                return;
            }

            var item = new MenuItem { Header = header };
            item.SetBinding(MenuItem.CommandProperty, new System.Windows.Data.Binding(commandName));
            menu.Items.Add(item);
        }

        if (DataContext is not SilenceableRowViewModel row)
        {
            return;
        }

        var directly = row.IsSilencedDirectly;

        Add("До восстановления", "SilenceUntilRecoveryCommand", row.CanSilenceUntilRecovery);
        Add("На 1 час", "SilenceOneHourCommand");
        Add("На 8 часов", "SilenceEightHoursCommand");
        Add("На сутки", "SilenceOneDayCommand");
        Add("Навсегда", "SilenceForeverCommand");

        if (directly)
        {
            menu.Items.Add(new Separator());
            Add("Вернуть оповещения", "UnsilenceCommand");
        }

        menu.IsOpen = true;
    }
}
