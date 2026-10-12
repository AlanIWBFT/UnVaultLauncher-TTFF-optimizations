using UnVault.Core.Epic;

namespace UnVault.Core.Tests;

/// <summary>The cache of manifests downloaded from Epic: kept small, and nothing but its own files is ever deleted.</summary>
public sealed class ManifestCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"unvault-manifests-{Guid.NewGuid():N}");
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private const string Hash = "6edc11fbb1bfac1c45580b1e0d9dee50f8c8b827";

    public ManifestCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A file of <paramref name="size"/> bytes, last used <paramref name="daysAgo"/> days before <see cref="Now"/>.</summary>
    private string File(string name, int size, double daysAgo)
    {
        string path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[size]);
        System.IO.File.SetLastWriteTimeUtc(path, Now.AddDays(-daysAgo));
        return path;
    }

    private string[] Left() => [.. Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(_dir, f)).Order()];

    [Fact]
    public void Unused_ones_go_after_a_while_and_the_oldest_beyond_the_limit()
    {
        File($"UE_5.0_{Hash}.manifest", 100, daysAgo: 40); // unused too long
        File($"UE_4.27_{Hash}.manifest", 100, daysAgo: 20);
        File($"Greybox_57_{Hash}.manifest", 100, daysAgo: 10);
        File($"UE_5.8_{Hash}.manifest", 100, daysAgo: 1);
        File($"UE_5.7_{Hash}.manifest.0123456789abcdef0123456789abcdef.tmp", 100, daysAgo: 35); // a write cut short

        var freed = ManifestCache.Prune(limitBytes: 250, _dir, Now);

        // 40 days: gone however much room there is. Then 300 bytes left for 250: the least recently used goes.
        Assert.Equal((3, 300L), freed);
        Assert.Equal([$"Greybox_57_{Hash}.manifest", $"UE_5.8_{Hash}.manifest"], Left());
        Assert.Equal(200, ManifestCache.Size(_dir));
    }

    [Fact]
    public void Nothing_but_its_own_files_is_ever_deleted()
    {
        string[] others =
        [
            "notes.txt",
            "UE_5.0.manifest", // no hash in the name
            $"UE_5.0_{Hash}.manifest.bak",
            Path.Combine("older", $"UE_4.26_{Hash}.manifest"), // not directly in the folder
        ];
        foreach (string name in others)
            File(name, 100, daysAgo: 400);

        Assert.Equal((0, 0L), ManifestCache.Prune(limitBytes: 0, _dir, Now));
        Assert.Equal(others.Order(), Left());
        Assert.Equal(0, ManifestCache.Size(_dir));
    }

    [Fact]
    public void One_used_again_is_kept_over_newer_ones()
    {
        string reused = File($"UE_4.27_{Hash}.manifest", 100, daysAgo: 25);
        File($"UE_5.8_{Hash}.manifest", 100, daysAgo: 1);

        ManifestCache.Touch(reused); // read back from the cache just now
        ManifestCache.Prune(limitBytes: 150, _dir, DateTime.UtcNow);

        Assert.Equal([$"UE_4.27_{Hash}.manifest"], Left());
    }

    [Fact]
    public void A_limit_of_zero_keeps_none()
    {
        File($"UE_5.8_{Hash}.manifest", 100, daysAgo: 0);
        File($"Greybox_57_{Hash}.manifest", 100, daysAgo: 0);

        Assert.Equal((2, 200L), ManifestCache.Prune(limitBytes: 0, _dir, Now));
        Assert.Empty(Left());
    }

    [Fact]
    public void A_missing_folder_has_nothing_to_prune() =>
        Assert.Equal((0, 0L), ManifestCache.Prune(0, Path.Combine(_dir, "missing"), Now));
}
