using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Win32;
using SkiaSharp;
using UnVault.App.Browser;
using UnVault.App.Services;
using UnVault.App.Views;

internal static class Program
{
    public static readonly GraphicsLog Log = new();
    public static string Mode = "deferred";
    public static CacheProbe? Cache;

    [STAThread]
    private static int Main(string[] args)
    {
        string requested = args.FirstOrDefault() ?? "deferred";
        if (requested.StartsWith("cache-", StringComparison.Ordinal))
        {
            Cache = new CacheProbe(requested, args.ElementAtOrDefault(1) ?? throw new ArgumentException("Supply an isolated cache-file path."));
            Mode = requested switch { "cache-hardware" => "hardware", "cache-deferred" => "deferred", _ => "warp" };
        }
        else Mode = requested;
        if (Mode is not ("deferred" or "warp" or "hardware")) throw new ArgumentException("Use deferred, warp or hardware.");
        CheckImageCodecs();
        Logger.Sink = Log;
        var options = new Win32PlatformOptions
        {
            UseDeferredHardwareRendering = Mode == "deferred",
            RenderingMode = [Win32RenderingMode.AngleEgl], // A silent software-renderer fallback must not pass this check.
            WarpProgramCache = Cache,
        };
        if (Mode == "warp")
            options.GraphicsAdapterSelectionCallback = adapters =>
            {
                for (int i = 0; i < adapters.Count; i++)
                    if (string.Equals(adapters[i].Description, "Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase)) return i;
                throw new InvalidOperationException("WARP adapter not found.");
            };
        try
        {
            return AppBuilder.Configure<SmokeApp>().UsePlatformDetect().With(options).WithInterFont().StartWithClassicDesktopLifetime(args);
        }
        finally { Cache?.Dispose(); }
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
        var window = new Window { Title = "UnVault static graphics check: " + Program.Mode, Width = 640, Height = 360, Content = scene };
        Program.Log.WindowHandle = window.TryGetPlatformHandle()!.Handle;
        WindowTransitions.PrepareFirstShow(window, () => Program.Cache?.EnablePersistence());
        desktop.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                if (Program.Mode == "deferred")
                {
                    await Program.Log.HardwareReady.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    if (GraphicsLog.IsCloaked(Program.Log.WindowHandle)) throw new Exception("Hardware takeover preceded the window reveal.");
                }
                for (int i = 0; i < 12; i++)
                {
                    window.Width = 640 + i * 5;
                    scene.Tick = i;
                    scene.InvalidateVisual();
                    if (Program.Cache is not null && i % 4 == 0) GC.Collect(); // native callbacks must remain rooted for the display lifetime
                    await Task.Delay(100);
                }
                if (scene.Frames < 6) throw new Exception("The window did not redraw after initialization/resize.");
                if (Program.Log.Error is { } error) throw new Exception(error);
                Program.Cache?.Check();
                await CheckBrowserAsync(window);
                Console.WriteLine($"PASS: {Program.Mode}, {scene.Frames} redraws; Skia/ANGLE rendering, font shaping, resize");
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
        string folder = Path.Combine(Path.GetTempPath(), "opencode", "unvault-static-browser", Guid.NewGuid().ToString("N"));
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
    public readonly TaskCompletionSource HardwareReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? Error;
    public IntPtr WindowHandle;
    public static bool IsCloaked(IntPtr window)
    {
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(window, 14, out int flags, sizeof(int)));
        return (flags & 1) != 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    public bool IsEnabled(LogEventLevel level, string area) => area == "OpenGL" || level >= LogEventLevel.Error;
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Log(level, area, source, messageTemplate, []);
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        Console.WriteLine($"[{level}] {area}: {messageTemplate} {string.Join("; ", propertyValues)}");
        if (messageTemplate == "Deferred rendering switched from WARP to hardware.")
        {
            if (IsCloaked(WindowHandle)) Error = "Hardware takeover occurred while the startup window was cloaked.";
            Program.Cache?.HardwareReady();
            HardwareReady.TrySetResult();
        }
        if (level >= LogEventLevel.Error || messageTemplate.Contains("Deferred hardware initialization failed", StringComparison.Ordinal))
        {
            Error = messageTemplate;
            HardwareReady.TrySetException(new Exception(messageTemplate));
        }
    }
}

internal sealed class CacheProbe : IWin32AngleProgramCache, IDisposable
{
    private readonly WarpProgramCache _cache;
    private readonly string _mode;
    private int _loads, _hits, _stores;
    private (int Loads, int Stores)? _atHardwareSwitch;

    public CacheProbe(string mode, string path)
    {
        if (mode is not ("cache-cold" or "cache-warm" or "cache-reject" or "cache-hardware" or "cache-deferred"))
            throw new ArgumentException("Unknown cache scenario.");
        if (mode is "cache-cold" or "cache-deferred" && File.Exists(path))
            throw new ArgumentException("Cold-cache checks require a new path.");
        if (mode is "cache-warm" or "cache-reject" && !File.Exists(path))
            throw new ArgumentException("Warm/rejection checks require a populated cache.");
        if (mode == "cache-reject") CorruptBinaries(path);
        _cache = new WarpProgramCache(path);
        _mode = mode;
    }

    public ReadOnlyMemory<byte> Load(ReadOnlySpan<byte> key)
    {
        Interlocked.Increment(ref _loads);
        var result = _cache.Load(key);
        if (!result.IsEmpty) Interlocked.Increment(ref _hits);
        return result;
    }

    public void Store(ReadOnlySpan<byte> key, ReadOnlySpan<byte> binary)
    {
        Interlocked.Increment(ref _stores);
        _cache.Store(key, binary);
    }

    public void EnablePersistence() => _cache.EnablePersistence();
    public void HardwareReady() => _atHardwareSwitch = (_loads, _stores);

    public void Check()
    {
        Console.WriteLine($"Cache {_mode}: loads={_loads}, hits={_hits}, stores={_stores}");
        bool passed = _mode switch
        {
            "cache-cold" => _loads > 0 && _hits == 0 && _stores > 0,
            "cache-warm" => _loads > 0 && _hits > 0 && _stores == 0,
            "cache-reject" => _hits > 0 && _stores > 0,
            "cache-hardware" => _loads == 0 && _stores == 0,
            "cache-deferred" => _stores > 0 && _atHardwareSwitch == (_loads, _stores),
            _ => false
        };
        if (!passed) throw new Exception("Program cache scenario failed.");
        Console.WriteLine("PASS: WARP program cache " + _mode);
    }

    private static void CorruptBinaries(string path)
    {
        // Preserve the storage envelope but invalidate each ANGLE compressed binary: the native cache must reject it.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x31504357) throw new Exception("Unexpected cache format.");
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            int keySize = reader.ReadInt32(), size = reader.ReadInt32();
            stream.Position += keySize;
            long start = stream.Position;
            int first = stream.ReadByte();
            stream.Position = start;
            stream.WriteByte((byte)(first ^ 0xFF));
            stream.Position = start + size;
        }
    }

    public void Dispose() => _cache.Dispose();
}
