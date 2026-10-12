using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using UnVault.App.Services;

namespace UnVault.App.Tests;

/// <summary>The window opens as it was last closed: its size, its place, and whether it was maximized.</summary>
public sealed class WindowPlacementTests : IDisposable
{
    /// <summary>Each test's own file: the app's is only written by the app itself, never by tests.</summary>
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"unvault-window-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void It_is_saved_and_read_back_and_a_damaged_one_is_ignored()
    {
        Assert.Null(WindowPlacement.Load(_file)); // none yet: the usual size

        var placement = new WindowPlacement(1280, 820, 2160, 140, Maximized: true);
        placement.Save(_file);
        Assert.Equal(placement, WindowPlacement.Load(_file));

        File.WriteAllText(_file, "{ not a placement");
        Assert.Null(WindowPlacement.Load(_file));
    }

    [Fact]
    public void It_goes_back_to_its_place_only_while_that_is_on_a_screen()
    {
        PixelRect[] screens = [new(0, 0, 1920, 1040), new(1920, 0, 2560, 1400)];

        Assert.True(WindowPlacement.IsOnScreen(new PixelPoint(2160, 140), screens)); // on the second monitor
        Assert.False(WindowPlacement.IsOnScreen(new PixelPoint(2160, 140), screens[..1])); // that monitor is gone
        Assert.False(WindowPlacement.IsOnScreen(new PixelPoint(1900, 100), screens[..1])); // too little of it on the screen to drag
    }

    [AvaloniaFact]
    public void It_opens_no_smaller_than_the_window_may_be_and_maximized_if_it_was()
    {
        var window = new Window { MinWidth = 784, MinHeight = 520 };

        new WindowPlacement(600, 900, 0, 0, Maximized: true).ApplyTo(window);

        Assert.Equal((784d, 900d, WindowState.Maximized), (window.Width, window.Height, window.WindowState));
    }

    [AvaloniaFact]
    public void Closing_saves_the_size_it_has_when_not_maximized()
    {
        var window = new Window { Width = 1000, Height = 700 };
        WindowPlacement.Remember(window, _file);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.WindowState = WindowState.Maximized;
        Dispatcher.UIThread.RunJobs();
        window.Close();

        var saved = WindowPlacement.Load(_file);
        Assert.Equal((1000d, 700d, true), (saved?.Width, saved?.Height, saved?.Maximized));
    }
}
