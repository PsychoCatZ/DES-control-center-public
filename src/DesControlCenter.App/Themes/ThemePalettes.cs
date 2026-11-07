using System.Windows.Media;

namespace DesControlCenter.App.Themes;

/// <summary>
/// The two colour sets. Every key X here corresponds to a <c>XColor</c> and a <c>XBrush</c> resource in
/// DarkTheme.xaml, and every window refers to those brushes with DynamicResource — so when the theme
/// changes and the brush is replaced, everything already on screen recolours at once. (The status colours
/// green/amber/red are deliberately the same in both themes and are not part of the palettes.)
/// </summary>
public static class ThemePalettes
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static IReadOnlyDictionary<string, Color> Dark { get; } = new Dictionary<string, Color>
    {
        ["Background"] = C("#0A0E14"),
        ["Surface"] = C("#10151D"),
        ["SurfaceRaised"] = C("#171D27"),
        ["Border"] = C("#28323F"),
        ["TextPrimary"] = C("#F2F5F9"),
        ["TextSecondary"] = C("#9FADBD"),
        ["Accent"] = C("#2F8CFF"),
        ["AccentHover"] = C("#4FA0FF"),
        ["OnAccent"] = C("#F2F5F9"),
        ["Selection"] = C("#1B3A5C"),
        ["Note"] = C("#E0B84A")
    };

    public static IReadOnlyDictionary<string, Color> Light { get; } = new Dictionary<string, Color>
    {
        ["Background"] = C("#F2F4F7"),
        ["Surface"] = C("#FFFFFF"),
        ["SurfaceRaised"] = C("#E8ECF1"),
        ["Border"] = C("#C9D1DB"),
        ["TextPrimary"] = C("#141A22"),
        ["TextSecondary"] = C("#55626F"),
        ["Accent"] = C("#1F6FDB"),
        ["AccentHover"] = C("#3C86EE"),
        ["OnAccent"] = C("#FFFFFF"),
        ["Selection"] = C("#CCE0FB"),
        ["Note"] = C("#8A5A00")
    };
}
