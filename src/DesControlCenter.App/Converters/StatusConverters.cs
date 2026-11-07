using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DesControlCenter.Core.Enums;

namespace DesControlCenter.App.Converters;

/// <summary>
/// Real filled-brush status dots, not emoji glyphs — WPF's TextFormatter does not reliably
/// render color (COLR/CPAL) emoji fonts, so a 🔴/🟡/🟢 character can silently show up as a flat
/// gray/black outline instead of its intended color depending on rendering path. A bound Ellipse
/// Fill has no such ambiguity.
/// </summary>
public sealed class HealthLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            HealthLevel.Problem => "ProblemBrush",
            HealthLevel.Warning => "WarningBrush",
            _ => "OkBrush"
        };
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolOnlineToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.Resources[value is true ? "OkBrush" : "ProblemBrush"];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ServiceStateToBrushConverter : IValueConverter
{
    // Neutral grey that reads on both the light and the dark theme.
    private static readonly System.Windows.Media.SolidColorBrush UnknownBrush = Freeze(new(System.Windows.Media.Color.FromRgb(0x8A, 0x97, 0xA8)));

    private static System.Windows.Media.SolidColorBrush Freeze(System.Windows.Media.SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ServiceState.Running => "OkBrush",
            ServiceState.Stopped => "WarningBrush",
            ServiceState.Failed => "ProblemBrush",
            _ => null
        };
        return key is null ? UnknownBrush : Application.Current.Resources[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Dot colour that stays neutral grey until the resource's first check has come back, so a freshly started
/// window shows "checking…" instead of a wall of red. values[0] = bool (online) or HealthLevel, values[1] = bool (checked).
/// </summary>
public sealed class CheckedBrushConverter : IMultiValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush PendingBrush = Freeze(new(System.Windows.Media.Color.FromRgb(0x8A, 0x97, 0xA8)));

    private static System.Windows.Media.SolidColorBrush Freeze(System.Windows.Media.SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not true)
        {
            return PendingBrush;
        }

        var key = values[0] switch
        {
            true => "OkBrush",
            false => "ProblemBrush",
            HealthLevel.Problem => "ProblemBrush",
            HealthLevel.Warning => "WarningBrush",
            _ => "OkBrush"
        };
        return Application.Current.Resources[key];
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A switch that is ON is green; off or unreadable is a calm neutral grey (a switch position is not a health signal).</summary>
public sealed class SwitchStateToBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush NeutralBrush = Freeze(new(System.Windows.Media.Color.FromRgb(0x8A, 0x97, 0xA8)));

    private static System.Windows.Media.SolidColorBrush Freeze(System.Windows.Media.SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DesControlCenter.Core.Status.SwitchState.On ? Application.Current.Resources["OkBrush"] : NeutralBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? false : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? false : true;
}

/// <summary>Collapses a section whose backing collection is empty — e.g. no External Services configured yet.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public static readonly CountToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Drives the VPS dashboard's column count from the available width, bound to a UniformGrid's
/// own ActualWidth. Thresholds assume a ~320px comfortable card width plus gutters — three
/// columns need roughly 1000px, two need roughly 650px, otherwise it collapses to one.
/// </summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    public static readonly WidthToColumnsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = value is double d ? d : 0;
        return width switch
        {
            >= 1560 => 5,
            >= 1200 => 4,
            >= 1000 => 3,
            >= 650 => 2,
            _ => 1
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
