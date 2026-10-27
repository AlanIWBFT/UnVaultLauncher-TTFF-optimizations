using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.Win32;

namespace UnVault.App.Views;

internal static class WindowTransitions
{
    private const int TransitionsForceDisabled = 3;
    private const int Cloak = 13;

    /// <summary>Reveal the first rendered frame without a DWM transition, then restore normal window behavior.</summary>
    public static void PrepareFirstShow(Window window, Action? onRevealed = null)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle)
            return;

        IntPtr hwnd = handle.Handle;
        int disabled = 1;
        if (DwmSetWindowAttribute(hwnd, TransitionsForceDisabled, ref disabled, sizeof(int)) < 0)
            return;
        var compositor = Compositor.TryGetDefaultCompositor();
        var firstFrame = window.PlatformImpl?.TryGetFeature<IWin32FirstFrameFeature>();
        int cloak = 1;
        bool cloaked = compositor is not null && DwmSetWindowAttribute(hwnd, Cloak, ref cloak, sizeof(int)) >= 0;
        // The native device may prepare concurrently, but resource replacement must wait for this window's reveal.
        var hardwareDeferral = cloaked ? firstFrame?.DeferHardwareSwitch() : null;
        var closed = new CancellationTokenSource();
        bool opened = false;
        window.Opened += RevealFirstFrame;
        window.Closed += OnClosed;

        void OnClosed(object? sender, EventArgs e)
        {
            closed.Cancel();
            hardwareDeferral?.Dispose();
            if (!opened)
            {
                window.Opened -= RevealFirstFrame;
                window.Closed -= OnClosed;
                closed.Dispose();
            }
        }

        async void RevealFirstFrame(object? sender, EventArgs e)
        {
            opened = true;
            window.Opened -= RevealFirstFrame;
            try
            {
                if (cloaked)
                {
                    if (firstFrame is not null)
                    {
                        await firstFrame.FirstFrameCommitted.WaitAsync(closed.Token).ConfigureAwait(false);
                        // WM_PAINT can synchronously wait for a later render batch on the UI thread. Reveal the
                        // already-committed frame using only its captured HWND, without queuing behind that wait.
                        closed.Token.ThrowIfCancellationRequested();
                        int visible = 0;
                        DwmSetWindowAttribute(hwnd, Cloak, ref visible, sizeof(int));
                    }
                    else
                    {
                        // Non-WinUI backends do not expose a native commit fence; retain the conservative fallback.
                        await compositor!.RequestCompositionBatchCommitAsync().Rendered.WaitAsync(closed.Token).ConfigureAwait(false);
                        await Dispatcher.UIThread.InvokeAsync(() => compositor.RequestCompositionBatchCommitAsync().Rendered)
                            .WaitAsync(closed.Token).ConfigureAwait(false);
                        await Task.Run(DwmFlush).ConfigureAwait(false);
                        await Dispatcher.UIThread.InvokeAsync(() => SetIfAlive(Cloak, 0), DispatcherPriority.Send, closed.Token)
                            .GetTask().ConfigureAwait(false);
                    }
                }
                // Keep transitions disabled until DWM has consumed the reveal, without blocking the UI thread.
                await Task.Run(DwmFlush).ConfigureAwait(false);
                onRevealed?.Invoke();
            }
            catch (OperationCanceledException)
            {
                // Dispatcher shutdown can abort the queued operation when the window closes during startup.
            }
            catch (Exception ex)
            {
                Trace.TraceError("Could not wait for the initial window frame: {0}", ex);
            }
            finally
            {
                hardwareDeferral?.Dispose();
                Dispatcher.UIThread.Post(() =>
                {
                    window.Closed -= OnClosed;
                    if (cloaked)
                        SetIfAlive(Cloak, 0);
                    SetIfAlive(TransitionsForceDisabled, 0);
                    closed.Dispose();
                });
            }
        }

        void SetIfAlive(int attribute, int value)
        {
            if (window.TryGetPlatformHandle()?.Handle == hwnd)
                DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
