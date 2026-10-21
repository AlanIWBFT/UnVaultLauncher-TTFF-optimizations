using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace UnVault.App.Browser;

/// <summary>
/// A windowed WebView2 using source-generated COM, without the managed SDK or its native WinRT component.
/// Construct, initialize, use and dispose on the same STA thread with a running UI message loop.
/// Dispose also covers closing the window while environment/controller creation is still in flight.
/// </summary>
internal sealed partial class WebViewHost : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<Action> _unsubscribe = [];
    private readonly HashSet<TaskCompletionSource<string>> _scripts = [];
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2? _browser;
    private bool _initializing;
    private bool _disposed;

    public event Action<BrowserNavigation>? NavigationStarting;
    public event Action<string>? NewWindowRequested;
    public event Action? SourceChanged;
    public event Action? NavigationCompleted;

    public string Source { get { VerifyThread(); return Browser.GetSource(); } }
    public bool IsVisible { set { VerifyThread(); _controller?.SetIsVisible(value ? 1 : 0); } }
    private ICoreWebView2 Browser => _browser ?? throw new InvalidOperationException("The browser is not initialized.");

    public async Task InitializeAsync(nint parent, string userDataFolder, string? browserFolder = null)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initializing)
            throw new InvalidOperationException("The browser can only be initialized once.");
        _initializing = true;

        try
        {
            var environmentCompletion = new EnvironmentCompleted();
            Marshal.ThrowExceptionForHR(CreateCoreWebView2EnvironmentWithOptions(browserFolder, userDataFolder, 0, environmentCompletion));
            var environment = await environmentCompletion.Task;
            ObjectDisposedException.ThrowIf(_disposed, this);

            var controllerCompletion = new ControllerCompleted();
            environment.CreateCoreWebView2Controller(parent, controllerCompletion);
            var controller = await controllerCompletion.Task;
            // The native callback owns the result until this continuation. Always close even if the window has gone.
            if (_disposed)
            {
                controller.Close();
                throw new ObjectDisposedException(nameof(WebViewHost));
            }
            _controller = controller;
            ((ICoreWebView2Controller2)controller).SetDefaultBackgroundColor(new NativeColor { A = 255, R = 14, G = 16, B = 20 });
            var browser = _browser = controller.GetCoreWebView2();
            var settings = browser.GetSettings();
            settings.SetAreDevToolsEnabled(0);
            settings.SetIsStatusBarEnabled(0);

            long navigation = browser.AddNavigationStarting(new NavigationStartingCallback(args =>
            {
                if (_disposed) { args.SetCancel(1); return; }
                var request = new BrowserNavigation(args.GetUri());
                NavigationStarting?.Invoke(request);
                args.SetCancel(request.Cancel ? 1 : 0);
            }));
            _unsubscribe.Add(() => browser.RemoveNavigationStarting(navigation));
            long source = browser.AddSourceChanged(new SourceChangedCallback(() => { if (!_disposed) SourceChanged?.Invoke(); }));
            _unsubscribe.Add(() => browser.RemoveSourceChanged(source));
            long completed = browser.AddNavigationCompleted(new NavigationCompletedCallback(() => { if (!_disposed) NavigationCompleted?.Invoke(); }));
            _unsubscribe.Add(() => browser.RemoveNavigationCompleted(completed));
            long popup = browser.AddNewWindowRequested(new NewWindowCallback(args =>
            {
                args.SetHandled(1);
                if (!_disposed) NewWindowRequested?.Invoke(args.GetUri());
            }));
            _unsubscribe.Add(() => browser.RemoveNewWindowRequested(popup));
            // This is also required with NavigationStarting checks: external protocol launches have their own event.
            var browser18 = (ICoreWebView2_18)browser;
            long external = browser18.AddLaunchingExternalUriScheme(new ExternalUriCallback());
            _unsubscribe.Add(() => browser18.RemoveLaunchingExternalUriScheme(external));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Navigate(string uri) { VerifyThread(); Browser.Navigate(uri); }

    public void SetBounds(int x, int y, int width, int height)
    {
        VerifyThread();
        _controller?.SetBounds(new NativeRect { Left = x, Top = y, Right = x + width, Bottom = y + height });
    }

    public async Task<string> ExecuteScriptAsync(string script)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _scripts.Add(completion);
        try
        {
            Browser.ExecuteScript(script, new ScriptCompleted(completion));
            return await completion.Task;
        }
        finally { _scripts.Remove(completion); }
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed) return;
        _disposed = true;
        foreach (var script in _scripts) script.TrySetCanceled();
        _scripts.Clear();
        try
        {
            // Break native event -> managed host -> native controller cycles before closing the controller.
            foreach (var remove in _unsubscribe)
            {
                try { remove(); }
                catch (COMException) { } // A crashed browser may already have disconnected its events.
            }
        }
        finally
        {
            _unsubscribe.Clear();
            try { _controller?.Close(); }
            finally
            {
                _controller = null;
                _browser = null;
                NavigationStarting = null;
                NewWindowRequested = null;
                SourceChanged = null;
                NavigationCompleted = null;
            }
        }
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("WebView2 must be used on its owning UI thread.");
    }

    [LibraryImport("WebView2Loader", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CreateCoreWebView2EnvironmentWithOptions(string? browserFolder, string userDataFolder, nint options, IEnvironmentCompletedHandler handler);
}

internal sealed class BrowserNavigation(string uri)
{
    public string Uri { get; } = uri;
    public bool Cancel { get; set; }
}

// Async continuations never run inside a native completion callback: WebView2 forbids reentrant message loops.
[GeneratedComClass]
internal sealed partial class EnvironmentCompleted : IEnvironmentCompletedHandler
{
    private readonly TaskCompletionSource<ICoreWebView2Environment> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<ICoreWebView2Environment> Task => _completion.Task;
    public void Invoke(int error, ICoreWebView2Environment? environment)
    {
        if (error < 0) _completion.TrySetException(Marshal.GetExceptionForHR(error)!);
        else if (environment is null) _completion.TrySetException(new InvalidOperationException("WebView2 returned no environment."));
        else _completion.TrySetResult(environment);
    }
}
[GeneratedComClass]
internal sealed partial class ControllerCompleted : IControllerCompletedHandler
{
    private readonly TaskCompletionSource<ICoreWebView2Controller> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<ICoreWebView2Controller> Task => _completion.Task;
    public void Invoke(int error, ICoreWebView2Controller? controller)
    {
        if (error < 0) _completion.TrySetException(Marshal.GetExceptionForHR(error)!);
        else if (controller is null) _completion.TrySetException(new InvalidOperationException("WebView2 returned no controller."));
        else _completion.TrySetResult(controller);
    }
}
[GeneratedComClass]
internal sealed partial class ScriptCompleted(TaskCompletionSource<string> completion) : IExecuteScriptHandler
{
    public void Invoke(int error, string? result)
    {
        if (error < 0) completion.TrySetException(Marshal.GetExceptionForHR(error)!);
        else completion.TrySetResult(result ?? "null");
    }
}
[GeneratedComClass]
internal sealed partial class NavigationStartingCallback(Action<INavigationStartingArgs> callback) : INavigationStartingHandler
{ public void Invoke(nint sender, INavigationStartingArgs args) => callback(args); }
[GeneratedComClass]
internal sealed partial class SourceChangedCallback(Action callback) : ISourceChangedHandler
{ public void Invoke(nint sender, nint args) => callback(); }
[GeneratedComClass]
internal sealed partial class NavigationCompletedCallback(Action callback) : INavigationCompletedHandler
{ public void Invoke(nint sender, nint args) => callback(); }
[GeneratedComClass]
internal sealed partial class NewWindowCallback(Action<INewWindowRequestedArgs> callback) : INewWindowRequestedHandler
{ public void Invoke(nint sender, INewWindowRequestedArgs args) => callback(args); }
[GeneratedComClass]
internal sealed partial class ExternalUriCallback : IExternalUriHandler
{ public void Invoke(nint sender, IExternalUriArgs args) => args.SetCancel(1); }
