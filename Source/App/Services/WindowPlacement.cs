using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using UnVault.Core;
using UnVault.Core.Util;

namespace UnVault.App.Services;

/// <summary>
/// The main window's size and place when it was last closed, kept in %LOCALAPPDATA%\UnVaultLauncher\window.json so it
/// opens the same way. The size is in the app's units (it follows the display's scaling), the place in screen pixels; both
/// are the window's when not maximized, so un-maximizing goes back to them.
/// </summary>
internal sealed record WindowPlacement(double Width, double Height, int X, int Y, bool Maximized)
{
    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "window.json");

    /// <summary>How far into the window a point must be on a screen for it to count as there: enough title bar to drag.</summary>
    private const int Grip = 48;

    /// <summary>The saved placement; null when there's none or it can't be read.</summary>
    public static WindowPlacement? Load(string? path = null)
    {
        path ??= FilePath;
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), WindowPlacementJSONContext.Default.WindowPlacement) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A placement that can't be saved only means the window opens at its usual size next time.</summary>
    public void Save(string? path = null)
    {
        path ??= FilePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, WindowPlacementJSONContext.Default.WindowPlacement));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Next time.
        }
    }

    /// <summary>
    /// Sets up <paramref name="window"/>, not yet shown, as it was: no smaller than it may be, and in its place only when
    /// that's still on a screen (a monitor may be gone since); elsewhere, where Windows puts it.
    /// </summary>
    public void ApplyTo(Window window)
    {
        window.Width = Math.Max(Width, window.MinWidth);
        window.Height = Math.Max(Height, window.MinHeight);
        var place = new PixelPoint(X, Y);
        if (IsOnScreen(place, window.Screens.All.Select(screen => screen.WorkingArea)))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = place;
        }
        if (Maximized)
            window.WindowState = WindowState.Maximized;
    }

    /// <summary>Whether a window at <paramref name="position"/> has enough of its title bar on a screen to be seen and dragged.</summary>
    internal static bool IsOnScreen(PixelPoint position, IEnumerable<PixelRect> workingAreas) =>
        workingAreas.Any(area => area.Contains(new PixelPoint(position.X + Grip, position.Y + Grip / 4)));

    /// <summary>Saves the window's placement each time it's closed (hidden to the tray too), as it was when last not maximized.</summary>
    public static void Remember(Window window, string? path = null)
    {
        var normal = (Size: new Size(window.Width, window.Height), window.Position);
        bool maximized = window.WindowState == WindowState.Maximized;
        void Note() => Dispatcher.UIThread.Post(() =>
        {
            // Once the change is over: maximizing resizes the window first, and only then says it's maximized.
            if (window.WindowState == WindowState.Normal)
                (normal, maximized) = ((window.ClientSize, window.Position), false);
            else if (window.WindowState == WindowState.Maximized)
                maximized = true;
        }, DispatcherPriority.Background);
        window.Opened += (_, _) => Note();
        window.Resized += (_, _) => Note();
        window.PositionChanged += (_, _) => Note();
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
                Note();
        };
        window.Closing += (_, _) =>
            new WindowPlacement(normal.Size.Width, normal.Size.Height, normal.Position.X, normal.Position.Y, maximized).Save(path);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WindowPlacement))]
internal sealed partial class WindowPlacementJSONContext : JsonSerializerContext;
