using Avalonia;
using Avalonia.Controls;

namespace UnVault.App.Views;

/// <summary>
/// Lays its children out in rows of equal columns: as many as fit at <see cref="MinColumnWidth"/>, up to
/// <see cref="MaxColumns"/>, so a narrow window gets one. Each row is as tall as its tallest child.
/// </summary>
public sealed class ColumnsPanel : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<ColumnsPanel, double>(nameof(MinColumnWidth), 400);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<ColumnsPanel, int>(nameof(MaxColumns), 2);

    /// <summary>Between columns, and between rows.</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ColumnsPanel, double>(nameof(Spacing), 12);

    static ColumnsPanel() => AffectsMeasure<ColumnsPanel>(MinColumnWidthProperty, MaxColumnsProperty, SpacingProperty);

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>How many columns a width this wide takes.</summary>
    public int ColumnsFor(double width) =>
        double.IsInfinity(width) ? 1 : Math.Clamp((int)((width + Spacing) / (MinColumnWidth + Spacing)), 1, Math.Max(1, MaxColumns));

    private readonly List<double> _rowHeights = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = ColumnsFor(availableSize.Width);
        double columnWidth = ColumnWidth(availableSize.Width, columns);
        _rowHeights.Clear();
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(columnWidth, double.PositiveInfinity));
            if (i % columns == 0)
                _rowHeights.Add(0);
            _rowHeights[^1] = Math.Max(_rowHeights[^1], Children[i].DesiredSize.Height);
        }
        double height = _rowHeights.Sum() + Spacing * Math.Max(0, _rowHeights.Count - 1);
        double width = double.IsInfinity(availableSize.Width) ? Children.Select(c => c.DesiredSize.Width).DefaultIfEmpty(0).Max() : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ColumnsFor(finalSize.Width);
        double columnWidth = ColumnWidth(finalSize.Width, columns);
        double y = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            int row = i / columns, column = i % columns;
            double rowHeight = row < _rowHeights.Count ? _rowHeights[row] : Children[i].DesiredSize.Height;
            Children[i].Arrange(new Rect(column * (columnWidth + Spacing), y, columnWidth, rowHeight));
            if (column == columns - 1 || i == Children.Count - 1)
                y += rowHeight + Spacing;
        }
        return finalSize;
    }

    private double ColumnWidth(double width, int columns) =>
        double.IsInfinity(width) ? MinColumnWidth : Math.Max(0, (width - Spacing * (columns - 1)) / columns);
}
