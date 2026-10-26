using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Threading;

namespace UnVault.App.Views;

/// <summary>
/// Shows its child, or folds it away: a change slides it open or shut, quickly, rather than jumping. On the page's first
/// showing it's already as it should be, without sliding.
/// </summary>
public sealed class FoldPanel : Decorator
{
    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<FoldPanel, bool>(nameof(IsOpen), true);

    /// <summary>How far open: 0 shut, 1 open, in between while it slides.</summary>
    public static readonly StyledProperty<double> OpennessProperty = AvaloniaProperty.Register<FoldPanel, double>(nameof(Openness), 1);

    public static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(180);

    /// <summary>A heading's arrow, turned with the slide: pointing down when open, right when shut.</summary>
    public static readonly IValueConverter ArrowAngle = new FuncValueConverter<double, double>(openness => (openness - 1) * 90);

    static FoldPanel()
    {
        AffectsMeasure<FoldPanel>(OpennessProperty);
        ClipToBoundsProperty.OverrideDefaultValue<FoldPanel>(true);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public double Openness
    {
        get => GetValue(OpennessProperty);
        set => SetValue(OpennessProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty)
        {
            if (IsOpen && Transitions is not null && Child is { IsVisible: false } hidden)
            {
                // Shut till now, what's in it may never have been laid out (folded since the page was built), and many
                // cards take a while. That's done first, still shut, so the slide starts after it: else its first frame
                // waits for them, and by then the slide is nearly over, as if it jumped open.
                hidden.IsVisible = true;
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsOpen)
                        Openness = 1;
                }, DispatcherPriority.Background);
            }
            else
            {
                Openness = IsOpen ? 1 : 0; // slides once it's on screen (see below); jumps before
                if (!IsOpen && Openness == 0 && Child is { } child)
                    child.IsVisible = false; // folded again before it started opening
            }
        }
        else if (change.Property == OpennessProperty && Child is { } child)
            child.IsVisible = Openness > 0; // shut: nothing in it can be tabbed to or read out
        else if (change.Property == ChildProperty && Child is { } added)
            added.IsVisible = Openness > 0;
    }

    /// <summary>Sliding from the first change after it's laid out; the state it was built in shows at once.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => Transitions ??=
            [new DoubleTransition { Property = OpennessProperty, Duration = SlideTime, Easing = new CubicEaseOut() }], DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Transitions = null;
    }

    /// <summary>
    /// The child at its full height, of which as much is shown as the panel is open. A child taller than the window slides
    /// only a window's height: the rest is out of sight below either way, and sliding all of it in the same time filled
    /// (or emptied) the part on screen at once.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child)
            return default;
        child.Measure(availableSize.WithHeight(double.PositiveInfinity));
        double full = child.DesiredSize.Height, openness = Math.Clamp(Openness, 0, 1);
        double slide = Math.Min(full, TopLevel.GetTopLevel(this)?.ClientSize.Height ?? full);
        return new Size(child.DesiredSize.Width, openness >= 1 ? full : slide * openness);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, Child.DesiredSize.Height));
        return finalSize;
    }
}
