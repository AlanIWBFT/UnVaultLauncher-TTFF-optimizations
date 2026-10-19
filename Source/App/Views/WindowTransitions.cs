using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UnVault.App.Views;

internal static class WindowTransitions
{
    private const int TransitionsForceDisabled = 3;

    /// <summary>Skip only the first DWM reveal transition; later minimize/restore transitions keep their normal behavior.</summary>
    public static void SkipFirstShow(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle)
            return;

        int disabled = 1;
        if (DwmSetWindowAttribute(handle.Handle, TransitionsForceDisabled, ref disabled, sizeof(int)) < 0)
            return;
        window.Opened += RestoreTransitions;

        async void RestoreTransitions(object? sender, EventArgs e)
        {
            window.Opened -= RestoreTransitions;
            // Opened runs after ShowWindow. Let DWM consume that show before restoring transitions,
            // without blocking the UI thread (or imposing an arbitrary startup delay).
            await Task.Run(DwmFlush).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (window.TryGetPlatformHandle()?.Handle != handle.Handle)
                    return; // The window may have closed while waiting for DWM.
                int enabled = 0;
                DwmSetWindowAttribute(handle.Handle, TransitionsForceDisabled, ref enabled, sizeof(int));
            });
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
