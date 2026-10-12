using System.Text.RegularExpressions;

namespace UnVault.Core.Epic;

/// <summary>
/// Manifests downloaded from Epic (engines', Fab items'), kept in %LOCALAPPDATA%\UnVaultLauncher\manifests so the same
/// build needn't be downloaded again. Only a speed-up: an install keeps its own copy in .unvault, a download its own in the
/// Vault Cache. A file's time is when it was last used: written, or read back.
/// </summary>
public static partial class ManifestCache
{
    public const int DefaultLimitMB = 512;

    /// <summary>Unused this long, a manifest goes, however much room there is.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <summary>
    /// What this cache writes, directly in its folder: "{app or artifact}_{SHA-1}.manifest", and the temporary files of
    /// writes cut short. Nothing else there is ever deleted.
    /// </summary>
    [GeneratedRegex(@"^.+_[0-9a-fA-F]{40}\.manifest(\.[0-9a-f]{32}\.tmp)?$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnFile();

    /// <summary>Marks a cached manifest as used now, so it's kept over ones that weren't.</summary>
    public static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Then it just looks older than it is.
        }
    }

    /// <summary>
    /// Deletes manifests unused for <see cref="MaxAge"/>, then the least recently used while the rest take more than
    /// <paramref name="limitBytes"/>. One being read, or that can't be deleted, stays.
    /// </summary>
    /// <param name="directory">Tests use folders of their own.</param>
    /// <returns>How many files went, and the room they took.</returns>
    public static (int Files, long Bytes) Prune(long limitBytes, string? directory = null, DateTime? now = null)
    {
        directory ??= AppPaths.ManifestCacheDirectory;
        if (!Directory.Exists(directory))
            return default;

        var options = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = FileAttributes.ReparsePoint };
        var files = new DirectoryInfo(directory).EnumerateFiles("*", options).Where(f => OwnFile().IsMatch(f.Name))
            .OrderBy(f => f.LastWriteTimeUtc).ToList();
        DateTime unusedSince = (now ?? DateTime.UtcNow) - MaxAge;
        long total = files.Sum(f => f.Length);
        int deleted = 0;
        long freed = 0;
        foreach (var file in files) // oldest first
        {
            if (file.LastWriteTimeUtc >= unusedSince && total <= limitBytes)
                break;
            try
            {
                long length = file.Length;
                file.Delete();
                total -= length;
                freed += length;
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use; next time.
            }
        }
        return (deleted, freed);
    }

    /// <summary>The room the cache takes now.</summary>
    public static long Size(string? directory = null)
    {
        directory ??= AppPaths.ManifestCacheDirectory;
        try
        {
            return Directory.Exists(directory)
                ? new DirectoryInfo(directory).EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint })
                    .Where(f => OwnFile().IsMatch(f.Name)).Sum(f => f.Length)
                : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
