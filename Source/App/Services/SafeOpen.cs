using System.ComponentModel;
using System.Diagnostics;

namespace UnVault.App.Services;

/// <summary>
/// Opens web pages and folders for the user. Windows "opens" whatever it's handed: a program, a script on a network share,
/// a link to another app (search-ms:, ms-msdt:…). Some of what the app opens comes from Epic's or GitHub's answers, or
/// from files it keeps, so a page opens only when it's https on a site the app links to, and a folder only when it's a
/// folder that's there.
/// </summary>
internal static class SafeOpen
{
    /// <summary>The sites the app links to, and their subdomains (www.fab.com, dev.epicgames.com).</summary>
    private static readonly string[] Sites = ["fab.com", "epicgames.com", "unrealengine.com", "github.com"];

    /// <summary>An https page on one of <see cref="Sites"/>, with no name and password before the site to disguise another.</summary>
    public static bool IsWebPage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.UserInfo.Length == 0
        && Sites.Any(site => uri.IdnHost.Equals(site, StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith("." + site, StringComparison.OrdinalIgnoreCase));

    /// <summary>A whole path to a folder that exists (a network one too: the user's own choice, as the Vault Cache).</summary>
    public static bool IsFolder(string? folder) =>
        !string.IsNullOrEmpty(folder) && Path.IsPathFullyQualified(folder) && Directory.Exists(folder);

    /// <summary>Opens the page in the default browser; false when it isn't one (<see cref="IsWebPage"/>) or can't be opened.</summary>
    public static bool WebPage(string? url) => IsWebPage(url) && Start(new Uri(url!).AbsoluteUri);

    /// <summary>Opens the folder as Windows opens folders (Explorer, unless the user chose another); false when it isn't one.</summary>
    public static bool Folder(string? folder) => IsFolder(folder) && Start(Path.GetFullPath(folder!));

    private static bool Start(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            return false; // no browser set up, say
        }
    }
}
