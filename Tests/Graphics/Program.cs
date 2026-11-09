using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using SkiaSharp;
using UnVault.App.Browser;

internal static class Program
{
    public static readonly GraphicsLog Log = new();

    [STAThread]
    private static int Main(string[] args)
    {
        CheckImageCodecs();
        Logger.Sink = Log;
        var options = new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] };
        return AppBuilder.Configure<SmokeApp>().UsePlatformDetect().With(options).WithInterFont().StartWithClassicDesktopLifetime(args);
    }

    private static void CheckImageCodecs()
    {
        using var bitmap = new SKBitmap(32, 32);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        byte[] jpeg = data.ToArray();
        Console.WriteLine($"JPEG encoded: {jpeg.Length} bytes");
        using var decoded = SKBitmap.Decode(jpeg);
        if (decoded is not { Width: 32, Height: 32 }) throw new Exception("JPEG round trip failed.");
        Console.WriteLine("PASS: JPEG round trip");
        // Exercise native decoder error recovery, including the statically resolved longjmp path.
        for (int length = 2; length < jpeg.Length; length += 17)
        {
            using var stream = new SKMemoryStream(jpeg.AsSpan(0, length).ToArray());
            using var codec = SKCodec.Create(stream);
            if (codec is not null)
            {
                using var partial = new SKBitmap(codec.Info);
                _ = codec.GetPixels(partial.Info, partial.GetPixels());
            }
        }
        Console.WriteLine("PASS: truncated-image error recovery");
    }
}

internal sealed class SmokeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var scene = new Scene();
        var window = new Window { Title = "UnVault Skia software check", Width = 640, Height = 360, Content = scene };
        desktop.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                for (int i = 0; i < 12; i++)
                {
                    window.Width = 640 + i * 5;
                    scene.Tick = i;
                    scene.InvalidateVisual();
                    await Task.Delay(100);
                }
                if (scene.Frames < 6) throw new Exception("The window did not redraw after initialization/resize.");
                if (Program.Log.Error is { } error) throw new Exception(error);
                using (var process = Process.GetCurrentProcess())
                    if (process.Modules.Cast<ProcessModule>().Any(m => m.ModuleName is "d3d11.dll" or "d3d10warp.dll" or "av_libglesv2.dll"))
                        throw new Exception("The software renderer loaded a D3D/ANGLE module.");
                await CheckBrowserAsync(window);
                Console.WriteLine($"PASS: {scene.Frames} redraws; Skia CPU rendering, font shaping, resize, no D3D/ANGLE modules before WebView2");
                desktop.Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                desktop.Shutdown(1);
            }
        };
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task CheckBrowserAsync(Window window)
    {
        // Exercise the static WebView2 Loader in the same process/CRT as the static graphics libraries.
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "unvault-static-browser", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string page = Path.Combine(folder, "page.html");
        File.WriteAllText(page, "<html><body>static-graphics-webview</body></html>");
        using var browser = new WebViewHost();
        await browser.InitializeAsync(window.TryGetPlatformHandle()!.Handle, Path.Combine(folder, "profile"));
        browser.SetBounds(20, 200, 450, 100);
        browser.IsVisible = true;
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.NavigationCompleted += () => loaded.TrySetResult();
        browser.Navigate(new Uri(page).AbsoluteUri);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
        if (await browser.ExecuteScriptAsync("document.body.innerText") != "\"static-graphics-webview\"")
            throw new Exception("Embedded browser script check failed.");
        Console.WriteLine("PASS: static graphics and WebView2 in one executable");
    }
}

internal sealed class Scene : Control
{
    public int Tick, Frames;
    public override void Render(DrawingContext context)
    {
        Frames++;
        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        context.FillRectangle(Brushes.CornflowerBlue, new Rect(20 + Tick * 8, 100, 90, 60));
        var text = new FormattedText("Static NativeAOT · 中文 · العربية", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Inter"), 24, Brushes.Black);
        context.DrawText(text, new Point(20, 25));
    }
}

internal sealed class GraphicsLog : ILogSink
{
    public string? Error;
    public bool IsEnabled(LogEventLevel level, string area) => area == "OpenGL" || level >= LogEventLevel.Error;
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Log(level, area, source, messageTemplate, []);
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        Console.WriteLine($"[{level}] {area}: {messageTemplate} {string.Join("; ", propertyValues)}");
        if (level >= LogEventLevel.Error)
        {
            Error = messageTemplate;
        }
    }
}
