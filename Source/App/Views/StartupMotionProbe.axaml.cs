using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace UnVault.App.Views;

public partial class StartupMotionProbe : UserControl
{
    public StartupMotionProbe() => InitializeComponent();
}

/// <summary>
/// A recording aid driven by the same monotonic clock in both processes. It exercises UI updates and rendering:
/// a stalled frame holds its old position, then jumps to the current phase instead of slowing down the animation.
/// </summary>
public sealed class StartupMotionAnimation : Control
{
    private TopLevel? _owner;
    private int _generation;
    private double _phase;

    public IBrush AccentBrush { get; set; } = Brushes.CornflowerBlue;
    public IBrush TrackBrush { get; set; } = Brushes.Gray;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _owner = TopLevel.GetTopLevel(this);
        if (_owner is not null)
            _owner.PropertyChanged += OwnerChanged;
        StartAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ++_generation;
        if (_owner is not null)
            _owner.PropertyChanged -= OwnerChanged;
        _owner = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
            StartAnimation();
    }

    private void StartAnimation()
    {
        int generation = ++_generation;
        if (_owner is not { IsVisible: true } owner)
            return;

        void Tick(TimeSpan _)
        {
            if (generation != _generation || !owner.IsVisible)
                return;
            _phase = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency % 2 / 2;
            InvalidateVisual();
            owner.RequestAnimationFrame(Tick);
        }
        owner.RequestAnimationFrame(Tick);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var track = new Pen(TrackBrush, 1);
        var hand = new Pen(AccentBrush, 2.5);
        var center = new Point(26, Bounds.Height / 2);
        const double radius = 21;
        context.DrawEllipse(null, track, center, radius, radius);
        for (int i = 0; i < 12; i++)
        {
            double angle = i * Math.Tau / 12;
            var direction = new Vector(Math.Sin(angle), -Math.Cos(angle));
            context.DrawLine(track, center + direction * (radius - 3), center + direction * radius);
        }
        double rotation = _phase * Math.Tau;
        context.DrawLine(hand, center, center + new Vector(Math.Sin(rotation), -Math.Cos(rotation)) * (radius - 3));
        context.DrawEllipse(AccentBrush, null, center, 3, 3);

        double left = 68, right = Math.Max(left, Bounds.Width - 8), y = Bounds.Height / 2;
        context.DrawLine(track, new Point(left, y), new Point(right, y));
        for (int i = 0; i <= 10; i++)
        {
            double x = left + (right - left) * i / 10;
            context.DrawLine(track, new Point(x, y - 5), new Point(x, y + 5));
        }
        double position = 1 - Math.Abs(_phase * 2 - 1);
        context.DrawEllipse(AccentBrush, null, new Point(left + (right - left) * position, y), 7, 7);
    }
}
