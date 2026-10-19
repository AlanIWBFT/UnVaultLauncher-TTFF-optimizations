#!/usr/bin/env dotnet
#:property PublishAot=false
#:package Avalonia.Desktop@12.1.3
#:package Avalonia.Win32@12.1.3.1
#:include ../../Source/App/Views/WindowTransitions.cs
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Threading;
using UnVault.App.Views;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException();
if (args.Length != 1 || args[0] is not ("reveal" or "close-early"))
    throw new ArgumentException("Expected reveal or close-early.");
FirstFrameApp.CloseEarly = args[0] == "close-early";
int result = 1;
var thread = new Thread(() =>
{
    try
    {
        result = AppBuilder.Configure<FirstFrameApp>().UsePlatformDetect()
            .With(new Win32PlatformOptions { UseDeferredHardwareRendering = true })
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
    public static bool CloseEarly;
    public static IntPtr WindowHandle;
    public static long DrawFinished;
    public static int Frames, RevealedDuringDraw;

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
        WindowTransitions.PrepareFirstShow(window);
        window.Opened += (_, _) => Console.WriteLine($"Opened: cloaked={IsCloaked()}");
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
                bool passed = finished != 0 && Volatile.Read(ref RevealedDuringDraw) == 0;
                Console.WriteLine($"Revealed: frames={Frames}, earlyRevealSamples={RevealedDuringDraw}, afterDrawMs=" +
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
                    lifetime.Shutdown(recloaked ? 1 : 0);
                }, TimeSpan.FromMilliseconds(100));
            };
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
            if (first && !FirstFrameApp.CloseEarly)
                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(20); // Deliberately slow the actual render-thread draw, not the UI-thread recording pass.
                    if (!FirstFrameApp.IsCloaked())
                        Interlocked.Increment(ref FirstFrameApp.RevealedDuringDraw);
                }
            context.DrawRectangle(new ImmutableSolidColorBrush(Colors.DarkSlateBlue), null, Bounds);
            if (first)
                Volatile.Write(ref FirstFrameApp.DrawFinished, Stopwatch.GetTimestamp());
        }
    }
}
