#!/usr/bin/env dotnet
#:property PublishAot=false
#:package Avalonia.Desktop@12.1.3
#:package Avalonia.Win32@12.1.3.3
#:include ../../Source/App/Views/WindowTransitions.cs
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Logging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Threading;
using Avalonia.Win32;
using UnVault.App.Views;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException();
if (args.Length != 1 || args[0] is not ("reveal" or "busy-ui" or "hardware" or "direct-composition" or "multiple-windows" or "close-before-show" or "close-early"))
    throw new ArgumentException("Expected reveal, busy-ui, hardware, direct-composition, multiple-windows, close-before-show, or close-early.");
FirstFrameApp.Mode = args[0];
int result = 1;
var thread = new Thread(() =>
{
    try
    {
        var options = new Win32PlatformOptions { UseDeferredHardwareRendering = FirstFrameApp.Mode != "hardware" };
        if (FirstFrameApp.Mode == "direct-composition")
            options.CompositionMode = [Win32CompositionMode.DirectComposition];
        Logger.Sink = new FirstFrameLog();
        result = AppBuilder.Configure<FirstFrameApp>().UsePlatformDetect().With(options)
            .StartWithClassicDesktopLifetime([]);
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

sealed class FirstFrameApp : Application
{
    public static string Mode = "";
    public static bool CloseEarly => Mode == "close-early";
    public static IntPtr WindowHandle;
    public static long DrawFinished;
    public static int Frames, RevealedDuringDraw, CommittedDuringDraw, Switches, EarlySwitches, RenderErrors;
    public static IWin32FirstFrameFeature? FirstFrame;
    public static readonly ManualResetEventSlim FirstDrawStarted = new(false);

    public static bool IsCloaked()
    {
        int hr = DwmGetWindowAttribute(WindowHandle, 14, out int flags, sizeof(int));
        Marshal.ThrowExceptionForHR(hr);
        return (flags & 1) != 0;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Title = "First-frame reveal smoke test", Width = 360, Height = 220, Content = new SlowPaint() };
        lifetime.MainWindow = window;
        WindowHandle = window.TryGetPlatformHandle()!.Handle;
        FirstFrame = window.PlatformImpl?.TryGetFeature<IWin32FirstFrameFeature>();
        WindowTransitions.PrepareFirstShow(window);
        window.Opened += (_, _) => Console.WriteLine($"Opened: cloaked={IsCloaked()}, nativeFence={FirstFrame is not null}");
        Window? other = null;
        IWin32FirstFrameFeature? otherFrame = null;
        if (Mode == "multiple-windows")
        {
            other = new Window { Title = "Independent first-frame fence", Width = 240, Height = 120,
                Content = new Border { Background = Brushes.DarkRed } };
            otherFrame = other.PlatformImpl?.TryGetFeature<IWin32FirstFrameFeature>();
            WindowTransitions.PrepareFirstShow(other);
        }
        if (Mode == "close-before-show")
        {
            lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var neverShown = new Window { Title = "Closed before its first show" };
            var unusedFrame = neverShown.PlatformImpl?.TryGetFeature<IWin32FirstFrameFeature>();
            WindowTransitions.PrepareFirstShow(neverShown);
            neverShown.Close();
            if (unusedFrame is not null && !unusedFrame.FirstFrameCommitted.IsCanceled)
                throw new InvalidOperationException("Closing an unshown window did not cancel its first-frame fence.");
            Console.WriteLine("Closed before show: first-frame wait canceled; hardware deferral must not block the main window.");
        }
        if (CloseEarly)
            DispatcherTimer.RunOnce(() => lifetime.Shutdown(0), TimeSpan.FromMilliseconds(50));
        else
        {
            bool revealed = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) =>
            {
                if (revealed || IsCloaked())
                    return;
                revealed = true;
                timer.Stop();
                long finished = Volatile.Read(ref DrawFinished);
                bool passed = finished != 0 && RevealedDuringDraw == 0 && CommittedDuringDraw == 0 &&
                    (FirstFrame is null || FirstFrame.FirstFrameCommitted.IsCompletedSuccessfully);
                if (otherFrame is not null)
                    passed &= !otherFrame.FirstFrameCommitted.IsCompleted && Switches == 0;
                Console.WriteLine($"Revealed: frames={Frames}, earlyRevealSamples={RevealedDuringDraw}, earlyCommitSamples={CommittedDuringDraw}, afterDrawMs=" +
                    (finished == 0 ? "not drawn" : Stopwatch.GetElapsedTime(finished).TotalMilliseconds.ToString("F1")));
                if (!passed)
                {
                    lifetime.Shutdown(1);
                    return;
                }
                window.Hide();
                DispatcherTimer.RunOnce(() =>
                {
                    window.Show();
                    bool recloaked = IsCloaked();
                    Console.WriteLine($"Second show: cloaked={recloaked}");
                    if (recloaked)
                    {
                        lifetime.Shutdown(1);
                        return;
                    }
                    other?.Show();
                    DispatcherTimer.RunOnce(() =>
                    {
                        bool expectedSwitch = Mode != "hardware";
                        bool otherCommitted = otherFrame is null || otherFrame.FirstFrameCommitted.IsCompletedSuccessfully;
                        Console.WriteLine($"Takeover: switches={Switches}, beforeReveal={EarlySwitches}, renderErrors={RenderErrors}, otherCommitted={otherCommitted}");
                        other?.Close();
                        lifetime.Shutdown((Switches > 0) == expectedSwitch && (FirstFrame is null || EarlySwitches == 0) && RenderErrors == 0 && otherCommitted ? 0 : 1);
                    }, TimeSpan.FromSeconds(1.5));
                }, TimeSpan.FromMilliseconds(100));
            };
            if (Mode == "busy-ui")
            {
                window.Opened += BlockUiOnFirstOpen;
                void BlockUiOnFirstOpen(object? sender, EventArgs e)
                {
                    window.Opened -= BlockUiOnFirstOpen;
                    // Queue a real frame before blocking the UI dispatcher, as SyncCommit does during WM_PAINT.
                    _ = Compositor.TryGetDefaultCompositor()!.RequestCompositionBatchCommitAsync();
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            if (FirstFrame is null || !FirstDrawStarted.Wait(TimeSpan.FromSeconds(2)))
                                throw new InvalidOperationException("The native first-frame draw did not start.");
                            bool initiallyCloaked = IsCloaked();
                            bool initiallyCommitted = FirstFrame.FirstFrameCommitted.IsCompleted;
                            var blocked = Stopwatch.StartNew();
                            bool revealedWhileBlocked = Task.Run(async () =>
                            {
                                await FirstFrame.FirstFrameCommitted.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                                var timeout = Stopwatch.StartNew();
                                while (IsCloaked() && timeout.Elapsed < TimeSpan.FromSeconds(1))
                                    Thread.Sleep(2);
                                return !IsCloaked();
                            }).GetAwaiter().GetResult();
                            Console.WriteLine($"Busy UI: initiallyCloaked={initiallyCloaked}, initiallyCommitted={initiallyCommitted}, " +
                                $"revealedWhileBlocked={revealedWhileBlocked}, blockedMs={blocked.Elapsed.TotalMilliseconds:F1}");
                            if (!initiallyCloaked || initiallyCommitted || !revealedWhileBlocked)
                                lifetime.Shutdown(1);
                            else
                                timer.Start();
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine(e);
                            lifetime.Shutdown(1);
                        }
                    }, DispatcherPriority.Background);
                }
            }
            else
                timer.Start();
            DispatcherTimer.RunOnce(() =>
            {
                Console.Error.WriteLine($"Timed out: frames={Frames}, cloaked={IsCloaked()}");
                lifetime.Shutdown(1);
            }, TimeSpan.FromSeconds(5));
        }
        base.OnFrameworkInitializationCompleted();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}

sealed class SlowPaint : Control
{
    public override void Render(DrawingContext context) => context.Custom(new Draw(new Rect(Bounds.Size)));

    private sealed class Draw(Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point point) => Bounds.Contains(point);
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            bool first = Interlocked.Increment(ref FirstFrameApp.Frames) == 1;
            if (first)
                FirstFrameApp.FirstDrawStarted.Set();
            if (first && !FirstFrameApp.CloseEarly)
                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(20); // Deliberately slow the actual render-thread draw, not the UI-thread recording pass.
                    if (!FirstFrameApp.IsCloaked())
                        Interlocked.Increment(ref FirstFrameApp.RevealedDuringDraw);
                    if (FirstFrameApp.FirstFrame?.FirstFrameCommitted.IsCompleted == true)
                        Interlocked.Increment(ref FirstFrameApp.CommittedDuringDraw);
                }
            context.DrawRectangle(new ImmutableSolidColorBrush(Colors.DarkSlateBlue), null, Bounds);
            if (first)
                Volatile.Write(ref FirstFrameApp.DrawFinished, Stopwatch.GetTimestamp());
        }
    }
}

sealed class FirstFrameLog : ILogSink
{
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Information;
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Log(level, area, source, messageTemplate, []);
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (messageTemplate.Contains("switched from WARP to hardware", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref FirstFrameApp.Switches);
            if (FirstFrameApp.IsCloaked())
                Interlocked.Increment(ref FirstFrameApp.EarlySwitches);
        }
        if (level >= LogEventLevel.Error)
        {
            Interlocked.Increment(ref FirstFrameApp.RenderErrors);
            Console.Error.WriteLine($"{area}: {messageTemplate} {string.Join(", ", propertyValues.Select(v => v?.ToString()))}");
        }
    }
}
