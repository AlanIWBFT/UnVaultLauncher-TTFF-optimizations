using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using UnVault.App.Views;

namespace UnVault.App.Tests;

/// <summary>A fold slides open and shut once it's on screen; as built, it's at once as it should be.</summary>
public sealed class FoldPanelTests
{
    private static (Window Window, FoldPanel Fold) Shown(bool open)
    {
        var fold = new FoldPanel { IsOpen = open, Child = new Border { Height = 100 } };
        var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { fold } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, fold);
    }

    [AvaloniaFact]
    public void As_built_it_shows_open_or_shut_without_sliding()
    {
        var (shutWindow, shut) = Shown(open: false);
        Assert.Equal((0d, false), (shut.Bounds.Height, shut.Child!.IsVisible)); // nothing in it to tab to
        var (openWindow, open) = Shown(open: true);
        Assert.Equal((100d, true), (open.Bounds.Height, open.Child!.IsVisible));
        shutWindow.Close();
        openWindow.Close();
    }

    /// <summary>
    /// Steps the clock (it follows real time here) until the slide settles; what it passed through on the way: how far open,
    /// or what <paramref name="sample"/> measures.
    /// </summary>
    internal static List<double> Slide(FoldPanel fold, Func<double>? sample = null)
    {
        var seen = new List<double>();
        var deadline = DateTime.UtcNow + FoldPanel.SlideTime * 10;
        do
        {
            Thread.Sleep(15);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            seen.Add(sample?.Invoke() ?? fold.Openness);
        }
        while (fold.Openness != (fold.IsOpen ? 1 : 0) && DateTime.UtcNow < deadline);
        return seen;
    }

    [AvaloniaFact]
    public void Folding_slides_it_shut_and_unfolding_open()
    {
        var (window, fold) = Shown(open: true);

        fold.IsOpen = false;
        Assert.Contains(Slide(fold), openness => openness is > 0.05 and < 0.95); // sliding, not jumping
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((0d, 0d, false), (fold.Openness, fold.Bounds.Height, fold.Child!.IsVisible));

        fold.IsOpen = true;
        Assert.Contains(Slide(fold), openness => openness is > 0.05 and < 0.95);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((1d, 100d, true), (fold.Openness, fold.Bounds.Height, fold.Child!.IsVisible));
        window.Close();
    }

    /// <summary>
    /// Shut since it was built, what's in it was never laid out, and many cards take a while: the slide waits for that,
    /// instead of starting at once and being nearly over by its first frame, as if it jumped open.
    /// </summary>
    [AvaloniaFact]
    public void Shut_since_it_was_built_it_still_slides_open()
    {
        var fold = new FoldPanel { IsOpen = false, Child = new SlowToLayOut() };
        var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { fold } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        fold.IsOpen = true;
        Assert.Contains(Slide(fold), openness => openness is > 0.05 and < 0.95);
        Assert.Equal(1d, fold.Openness);
        window.Close();
    }

    /// <summary>As many cards are, the first time: its first layout takes longer than the whole slide.</summary>
    private sealed class SlowToLayOut : Control
    {
        private bool _laidOut;

        protected override Size MeasureOverride(Size availableSize)
        {
            if (!_laidOut)
                Thread.Sleep(FoldPanel.SlideTime * 2);
            _laidOut = true;
            return new Size(100, 100);
        }
    }

    [Theory]
    [InlineData(1d, 0d)] // open: pointing down
    [InlineData(0d, -90d)] // shut: pointing right
    [InlineData(0.5d, -45d)]
    public void The_headings_arrow_turns_with_the_slide(double openness, double angle) =>
        Assert.Equal(angle, FoldPanel.ArrowAngle.Convert(openness, typeof(double), null, System.Globalization.CultureInfo.InvariantCulture));
}
