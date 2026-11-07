using System.Windows;
using System.Windows.Controls;

namespace DesControlCenter.App.Views;

/// <summary>
/// The server overview: collapsed cards fill rows left to right in equal columns (so seven servers
/// on a wide monitor read as 4 + 3, in a stable order); an expanded card takes a whole row of its
/// own, so its resources get room and its neighbours neither move into its column nor leave a gap.
/// A card announces that it is expanded through the <see cref="ExpandedProperty"/> attached property
/// (set from the item container style).
/// </summary>
public sealed class ServerGridPanel : Panel
{
    public static readonly DependencyProperty ExpandedProperty = DependencyProperty.RegisterAttached(
        "Expanded", typeof(bool), typeof(ServerGridPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static bool GetExpanded(DependencyObject element) => (bool)element.GetValue(ExpandedProperty);
    public static void SetExpanded(DependencyObject element, bool value) => element.SetValue(ExpandedProperty, value);

    public static readonly DependencyProperty MinCardWidthProperty = DependencyProperty.Register(
        nameof(MinCardWidth), typeof(double), typeof(ServerGridPanel),
        new FrameworkPropertyMetadata(300.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(ServerGridPanel),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinCardWidth
    {
        get => (double)GetValue(MinCardWidthProperty);
        set => SetValue(MinCardWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    private readonly List<Rect> _slots = new();

    /// <summary>How many equal columns fit in <paramref name="width"/> (never fewer than 1, never more than <paramref name="maxColumns"/>).</summary>
    public static int ColumnsFor(double width, double minCardWidth, int maxColumns) =>
        Math.Max(1, Math.Min(Math.Max(1, maxColumns), (int)Math.Floor(width / Math.Max(1, minCardWidth))));

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var columns = width > 0 ? ColumnsFor(width, MinCardWidth, MaxColumns) : 1;
        var columnWidth = width > 0 ? width / columns : double.PositiveInfinity;

        _slots.Clear();
        var row = new List<int>();
        var rowHeight = 0.0;
        var top = 0.0;

        void FlushRow()
        {
            if (row.Count == 0)
            {
                return;
            }

            for (var c = 0; c < row.Count; c++)
            {
                var child = InternalChildren[row[c]];
                _slots[row[c]] = new Rect(c * columnWidth, top, columnWidth, rowHeight);
            }

            top += rowHeight;
            row.Clear();
            rowHeight = 0;
        }

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            _slots.Add(Rect.Empty);

            if (GetExpanded(child))
            {
                FlushRow();
                child.Measure(new Size(width > 0 ? width : double.PositiveInfinity, double.PositiveInfinity));
                row.Add(i);
                rowHeight = child.DesiredSize.Height;
                // a full-width row: one slot as wide as the whole panel
                _slots[i] = new Rect(0, top, width, rowHeight);
                top += rowHeight;
                row.Clear();
                rowHeight = 0;
                continue;
            }

            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            row.Add(i);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (row.Count == columns)
            {
                FlushRow();
            }
        }

        FlushRow();
        return new Size(width, top);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count && i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (slot.IsEmpty)
            {
                continue;
            }

            InternalChildren[i].Arrange(slot.Width <= 0 ? new Rect(slot.X, slot.Y, finalSize.Width, slot.Height) : slot);
        }

        return finalSize;
    }
}
