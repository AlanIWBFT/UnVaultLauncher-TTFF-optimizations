#:property PublishAot=false
#:package Avalonia.Desktop@12.1.3
#:package Avalonia.Win32@12.1.3.2
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("This smoke test exercises the Windows D3D backend.");
if (args.Length != 1 || args[0] is not ("switch" or "hardware-failure" or "close-early"))
    throw new ArgumentException("Expected switch, hardware-failure, or close-early.");
SmokeApp.Mode = args[0];
int result = 1;
var thread = new Thread(() =>
{
    try
    {
        var options = new Win32PlatformOptions { UseDeferredHardwareRendering = true };
        if (SmokeApp.Mode == "hardware-failure")
            options.GraphicsAdapterSelectionCallback = _ => throw new InvalidOperationException("Injected hardware initialization failure");
        Logger.Sink = SmokeApp.Log;
        result = AppBuilder.Configure<SmokeApp>().UsePlatformDetect().With(options).StartWithClassicDesktopLifetime([]);
    }
    catch (Exception e)
    {
        Console.Error.WriteLine(e);
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return result;

sealed class SmokeApp : Application
{
    public static string Mode = "";
    public static readonly SmokeLog Log = new();
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Title = "Deferred rendering smoke test", Width = 320, Height = 200,
            Content = new Border { Background = Brushes.DarkSlateBlue } };
        lifetime.MainWindow = window;
        int rendered = 0;
        async void RenderBatch()
        {
            try
            {
                await Compositor.TryGetDefaultCompositor()!.RequestCompositionBatchCommitAsync().Rendered;
                Interlocked.Increment(ref rendered);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                lifetime.Shutdown(1);
            }
        }
        window.Opened += (_, _) => RenderBatch();
        if (Mode == "close-early")
            DispatcherTimer.RunOnce(() => lifetime.Shutdown(0), TimeSpan.FromMilliseconds(50));
        else
        {
            DispatcherTimer.RunOnce(() =>
            {
                window.Width += 100;
                window.Height += 60;
                ((Border)window.Content!).Background = Brushes.DarkOliveGreen;
                RenderBatch();
            }, TimeSpan.FromSeconds(1.5));
            DispatcherTimer.RunOnce(() =>
            {
                bool switched = Log.Messages.Any(m => m.Contains("switched from WARP to hardware"));
                bool failed = Log.Messages.Any(m => m.Contains("Deferred hardware initialization failed"));
                bool expected = Mode == "switch" ? switched && !failed : failed && !switched;
                bool passed = expected && Volatile.Read(ref rendered) >= 2 && !Log.Errors.Any();
                Console.WriteLine($"{Mode}: rendered={rendered}, switched={switched}, fallback={failed}, errors={Log.Errors.Count}");
                lifetime.Shutdown(passed ? 0 : 1);
            }, TimeSpan.FromSeconds(3));
        }
        base.OnFrameworkInitializationCompleted();
    }
}

sealed class SmokeLog : ILogSink
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public ConcurrentQueue<string> Errors { get; } = new();
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Information;
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Log(level, area, source, messageTemplate, []);
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        Messages.Enqueue(messageTemplate);
        if (level >= LogEventLevel.Error)
            Errors.Enqueue(messageTemplate);
        if (area == "OpenGL" || level >= LogEventLevel.Error)
            Console.WriteLine($"{area}: {messageTemplate} {string.Join(", ", propertyValues.Select(v => v?.ToString()))}");
    }
}
