using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UnVault.App.Browser;
using UnVault.Core;
using UnVault.Core.Epic;

namespace UnVault.App.Views;

/// <summary>
/// Epic sign-in inside the app. On Windows an embedded WebView2 shows Epic's own page; when it reaches the
/// page that hands out the authorization code, we read the code and close — nothing to copy. The browser
/// profile is UnVault's own (separate from your normal browser), cleared on sign-out. There's no address bar to type in,
/// but one shows which site each page is from, as the password is typed here.
/// </summary>
public partial class LoginWindow : Window
{
    private const string CodePagePrefix = "https://www.epicgames.com/id/api/redirect";

    private readonly WebViewHost _browser = new();
    private bool _closed;
    private string? _code;

    public LoginWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Opened += async (_, _) => await StartAsync();
        SizeChanged += (_, _) => UpdateBrowserBounds();
        Closed += (_, _) => { _closed = true; _browser.Dispose(); };
    }


    /// <summary>Shows the window and returns the authorization code, or null if the user closed it.</summary>
    public async Task<string?> ShowAndWaitAsync(Window owner)
    {
        await ShowDialog(owner);
        return _code;
    }

    private async Task StartAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            ShowFallback(Strings.SignInNeedsWindows);
            return;
        }

        try
        {
            // A sign-out that couldn't delete the browser's data: finish it, or the previous account would be signed in again.
            await Task.Run(SignInBrowser.ClearPendingAsync);
            if (_closed) return;

            var handle = TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("No native window handle.");
            await _browser.InitializeAsync(handle, SignInBrowser.DataDirectory);
            if (_closed) return;
            AddressBar.IsVisible = true;
            UpdateBrowserBounds();

            var browser = _browser;
            browser.NavigationStarting += e =>
            {
                // Secure pages only, so nothing on the way can read or change what's typed; links that open other apps are refused.
                if (!IsSecure(e.Uri))
                    e.Cancel = true;
                // Epic's page with the code is raw text (the code in JSON): read, not shown.
                else if (IsCodePage(e.Uri))
                    ShowBrowser(false, Strings.SigningIn);
            };
            // Social logins (Google, Steam…) open popups; keep them in this window, where the address bar shows their site.
            browser.NewWindowRequested += uri =>
            {
                if (IsSecure(uri))
                    browser.Navigate(uri);
            };
            browser.SourceChanged += () => ShowSite(browser.Source);
            browser.NavigationCompleted += async () =>
            {
                try { await CheckForCodeAsync(browser); }
                catch (OperationCanceledException) when (_closed) { }
            };
            browser.Navigate(EpicEndpoints.LoginURL);
            LoadingPanel.IsVisible = false;
        }
        catch (ObjectDisposedException) when (_closed) { }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            if (_closed) return;
            ShowFallback(Localized.Format(Strings.BrowserUnavailable, ex.Message));
        }
    }

    private async Task CheckForCodeAsync(WebViewHost browser)
    {
        if (!IsCodePage(browser.Source))
            return;

        // The page body is the JSON with the code; ExecuteScriptAsync returns it as a JSON string literal.
        string result = await browser.ExecuteScriptAsync("document.body.innerText");
        if (_closed) return;
        using var document = JsonDocument.Parse(result);
        string text = document.RootElement.GetString() ?? "";
        string code = EpicAuthClient.ExtractAuthorizationCode(text);
        if (IsCode(code))
        {
            _code = code;
            Close();
        }
        else
        {
            ShowBrowser(true); // no code: whatever Epic says instead is worth reading
        }
    }

    internal static bool IsCodePage(string address) => address.StartsWith(CodePagePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The browser is a window of its own over this one; while it's hidden, the loading panel under it shows.</summary>
    private void ShowBrowser(bool shown, string? loadingText = null)
    {
        _browser.IsVisible = shown;
        LoadingPanel.IsVisible = !shown;
        if (loadingText is not null)
            LoadingText.Text = loadingText;
    }

    internal static bool IsSecure(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// The site a page is from, as a browser's address bar shows it: "www.epicgames.com". Letters that only look like
    /// ordinary ones are spelled out (xn--…), so a look-alike site can't pass for Epic's.
    /// </summary>
    internal static string SiteName(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.IdnHost.Length > 0 ? uri.IdnHost : address;

    private void ShowSite(string address)
    {
        AddressText.Text = SiteName(address);
        LockIcon.IsVisible = IsSecure(address);
    }

    /// <summary>The browser is a window of its own over this one, so it's placed by hand: everything below the address bar.</summary>
    private void UpdateBrowserBounds()
    {
        double scale = RenderScaling;
        double top = AddressBar.IsVisible ? AddressBar.Height : 0;
        _browser.SetBounds(0, (int)(top * scale), (int)(ClientSize.Width * scale), (int)((ClientSize.Height - top) * scale));
    }

    private void ShowFallback(string reason)
    {
        LoadingPanel.IsVisible = false;
        FallbackReason.Text = reason;
        FallbackPanel.IsVisible = true;
    }

    private void OnOpenBrowserClick(object? sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(EpicEndpoints.LoginURL) { UseShellExecute = true });

    private void OnSubmitCodeClick(object? sender, RoutedEventArgs e)
    {
        string code = EpicAuthClient.ExtractAuthorizationCode(CodeBox.Text ?? "");
        if (!IsCode(code))
        {
            FallbackReason.Text = Strings.NotTheCode;
            return;
        }
        _code = code;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private static bool IsCode(string value) => Regex.IsMatch(value, "^[0-9a-fA-F]{32}$");
}
