using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace UnVault.App.Views;

internal static class WindowTransitions
{
    private const int TransitionsForceDisabled = 3;
    private const int Cloak = 13;

    /// <summary>Reveal the first rendered frame without a DWM transition, then restore normal window behavior.</summary>
    public static void PrepareFirstShow(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle)
            return;

        int disabled = 1;
        if (DwmSetWindowAttribute(handle.Handle, TransitionsForceDisabled, ref disabled, sizeof(int)) < 0)
            return;
        var compositor = Compositor.TryGetDefaultCompositor();
        int cloak = 1;
        bool cloaked = compositor is not null && DwmSetWindowAttribute(handle.Handle, Cloak, ref cloak, sizeof(int)) >= 0;
        window.Opened += RevealFirstFrame;

        async void RevealFirstFrame(object? sender, EventArgs e)
        {
            window.Opened -= RevealFirstFrame;
            try
            {
                if (cloaked)
                {
                    await compositor!.RequestCompositionBatchCommitAsync().Rendered.ConfigureAwait(false);
                    // WinUIComposition queues its native commit after the rendering callback returns.
                    // A second rendering turn crosses that boundary before we flush the queued DWM work.
                    await Dispatcher.UIThread.InvokeAsync(() => compositor.RequestCompositionBatchCommitAsync().Rendered).ConfigureAwait(false);
                    await Task.Run(DwmFlush).ConfigureAwait(false);
                    await Dispatcher.UIThread.InvokeAsync(() => SetIfAlive(Cloak, 0)).GetTask().ConfigureAwait(false);
                }
                // Keep transitions disabled until DWM has consumed the reveal, without blocking the UI thread.
                await Task.Run(DwmFlush).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Dispatcher shutdown can abort the queued operation when the window closes during startup.
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (cloaked)
                        SetIfAlive(Cloak, 0);
                    SetIfAlive(TransitionsForceDisabled, 0);
                });
            }
        }

        void SetIfAlive(int attribute, int value)
        {
            if (window.TryGetPlatformHandle()?.Handle == handle.Handle)
                DwmSetWindowAttribute(handle.Handle, attribute, ref value, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
