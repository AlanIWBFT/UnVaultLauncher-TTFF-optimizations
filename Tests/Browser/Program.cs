using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using UnVault.App.Browser;

// Run the published EXE to exercise the actual NativeAOT/COM boundary, not just a managed test host.
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Marshal.ThrowExceptionForHR(Native.CoInitializeEx(0, 2));
        var context = new PumpContext();
        SynchronizationContext.SetSynchronizationContext(context);
        nint window = Native.CreateWindowExW(0, "STATIC", "UnVault browser smoke", 0x00CF0000, 0, 0, 800, 600, 0, 0, 0, 0);
        if (window == 0) throw new InvalidOperationException("Could not create the test HWND.");
        try
        {
            Task work = CheckAsync(window);
            var timeout = Stopwatch.StartNew();
            while (!work.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(90))
            {
                while (Native.PeekMessageW(out var message, 0, 0, 0, 1))
                {
                    Native.TranslateMessage(in message);
                    Native.DispatchMessageW(in message);
                }
                context.Drain();
                Thread.Sleep(5);
            }
            if (!work.IsCompleted) throw new TimeoutException("WebView2 smoke timed out.");
            work.GetAwaiter().GetResult();
            Console.WriteLine("PASS: all browser smoke checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Native.DestroyWindow(window);
            Native.CoUninitialize();
        }
    }

    private static async Task CheckAsync(nint window)
    {
        string directory = Path.Combine(Path.GetTempPath(), "opencode", "unvault-com-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string page = Path.Combine(directory, "page.html");
        File.WriteAllText(page, "<html><body>COM 源生成测试</body></html>");
        string uri = new Uri(page).AbsoluteUri;
        using var browser = new WebViewHost();
        await browser.InitializeAsync(window, Path.Combine(directory, "profile"));
        browser.SetBounds(0, 0, 800, 600);
        browser.IsVisible = true;
        int starting = 0, sources = 0;
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.NavigationStarting += e => { starting++; if (e.Uri.Contains("blocked.html", StringComparison.Ordinal)) e.Cancel = true; };
        browser.SourceChanged += () => sources++;
        browser.NavigationCompleted += () => loaded.TrySetResult();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        browser.Navigate(uri);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Require(starting > 0 && sources > 0 && browser.Source == uri, "Navigation and source events after GC");
        using (var text = JsonDocument.Parse(await browser.ExecuteScriptAsync("document.body.innerText")))
            Require(text.RootElement.GetString() == "COM 源生成测试", "UTF-16 script result");
        Console.WriteLine("PASS: initialize, bounds/color/settings, navigation, source, UTF-16 script, callback lifetime");

        browser.SetBounds(0, 20, 640, 460);
        using (var size = JsonDocument.Parse(await browser.ExecuteScriptAsync("[innerWidth * devicePixelRatio, innerHeight * devicePixelRatio]")))
        {
            Require(Math.Abs(size.RootElement[0].GetDouble() - 640) < 2, "Resized width");
            Require(Math.Abs(size.RootElement[1].GetDouble() - 460) < 2, "Resized height");
        }
        loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.Navigate(new Uri(Path.Combine(directory, "blocked.html")).AbsoluteUri);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Require(browser.Source == uri, "Cancelled navigation stays on original page");
        var popup = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.NewWindowRequested += target => popup.TrySetResult(target);
        await browser.ExecuteScriptAsync("window.open('https://example.invalid/popup')");
        Require(await popup.Task.WaitAsync(TimeSpan.FromSeconds(10)) == "https://example.invalid/popup", "New-window event");
        Console.WriteLine("PASS: resize, cancelled navigation, handled popup");

        Task<string> script = browser.ExecuteScriptAsync("document.body.innerText");
        browser.Dispose();
        try { await script; throw new Exception("Pending script was not cancelled on close."); }
        catch (OperationCanceledException) { }
        using var early = new WebViewHost();
        Task initializing = early.InitializeAsync(window, Path.Combine(directory, "profile"));
        early.Dispose();
        try { await initializing; throw new Exception("Closed initialization unexpectedly succeeded."); }
        catch (ObjectDisposedException) { }
        using var reopened = new WebViewHost();
        await reopened.InitializeAsync(window, Path.Combine(directory, "profile"));
        reopened.Dispose();
        Console.WriteLine("PASS: close with pending script, close during initialization, reopen");

        using var missing = new WebViewHost();
        try
        {
            await missing.InitializeAsync(window, Path.Combine(directory, "missing-profile"), Path.Combine(directory, "missing-runtime"));
            throw new Exception("Missing runtime unexpectedly succeeded.");
        }
        catch (FileNotFoundException) { Console.WriteLine("PASS: missing runtime maps to FileNotFoundException"); }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("FAILED: " + description);
    }
}

internal sealed class PumpContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue((callback, state));
    public void Drain() { while (_queue.TryDequeue(out var item)) item.Callback(item.State); }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("ole32.dll")] internal static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] internal static extern void CoUninitialize();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool PeekMessageW(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(in Message message);
    [DllImport("user32.dll")] internal static extern nint DispatchMessageW(in Message message);
}
