using System.Windows;
using System.Windows.Controls;
using DesControlCenter.App.ViewModels;

namespace DesControlCenter.App.Views;

/// <summary>Picks the card for the mixed AI row: a checked site gets the full card, a local app gets the plain launcher card.</summary>
public sealed class AiCardTemplateSelector : DataTemplateSelector
{
    public DataTemplate? WebsiteTemplate { get; set; }
    public DataTemplate? LauncherTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is LauncherLinkViewModel ? LauncherTemplate : WebsiteTemplate;
}
