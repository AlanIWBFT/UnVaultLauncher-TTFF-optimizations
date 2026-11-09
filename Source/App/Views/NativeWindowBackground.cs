using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace UnVault.App.Views;

internal static class NativeWindowBackground
{
    private const int BackgroundBrush = -10;

    /// <summary>Match native background erasure to the window's solid background before its first software frame.</summary>
    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.Background is not ISolidColorBrush background ||
            window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle)
            return;

        var color = background.Color;
        var brush = CreateSolidBrush((uint)(color.R | color.G << 8 | color.B << 16));
        if (brush == IntPtr.Zero)
            return;

        // Apply once: Avalonia registers a separate class per window, initially without a background brush.
        // Windows releases the installed brush when Avalonia unregisters that class.
        if (SetClassLongPtr(handle.Handle, BackgroundBrush, brush) == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            DeleteObject(brush);
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", SetLastError = true)]
    private static extern IntPtr SetClassLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);
}
