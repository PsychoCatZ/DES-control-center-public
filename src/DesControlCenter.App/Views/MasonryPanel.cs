using System.Windows;
using System.Windows.Controls;

namespace DesControlCenter.App.Views;

/// <summary>
/// Cards of different heights in N equal-width columns, each new card going into the currently
/// shortest column ("masonry"). A UniformGrid makes every ROW as tall as its tallest card, so with
/// six servers in five columns the sixth hung far below the first one with a huge gap beside it.
/// Here it simply fills the shortest column, so nothing is left empty.
/// </summary>
public sealed class MasonryPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    private (int Column, double Top)[] _slots = Array.Empty<(int, double)>();

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Math.Max(1, Columns);
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var columnWidth = width > 0 ? width / columns : 0;
        var heights = new double[columns];
        _slots = new (int, double)[InternalChildren.Count];

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(columnWidth > 0 ? columnWidth : double.PositiveInfinity, double.PositiveInfinity));

            var column = ShortestColumn(heights);
            _slots[i] = (column, heights[column]);
            heights[column] += child.DesiredSize.Height;
        }

        return new Size(width, heights.Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Math.Max(1, Columns);
        var columnWidth = finalSize.Width / columns;

        for (var i = 0; i < InternalChildren.Count && i < _slots.Length; i++)
        {
            var child = InternalChildren[i];
            var (column, top) = _slots[i];
            child.Arrange(new Rect(column * columnWidth, top, columnWidth, child.DesiredSize.Height));
        }

        return finalSize;
    }

    private static int ShortestColumn(double[] heights)
    {
        var best = 0;
        for (var c = 1; c < heights.Length; c++)
        {
            // Strictly shorter, so on a tie the leftmost column wins and the first row reads left to right.
            if (heights[c] < heights[best])
            {
                best = c;
            }
        }

        return best;
    }
}
