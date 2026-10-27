using UnVault.App.Services;

namespace UnVault.App.Tests;

public sealed class WarpProgramCacheTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "opencode", "unvault-warp-cache-tests", Guid.NewGuid().ToString("N"));
    private string CachePath => Path.Combine(_folder, "warp.bin");

    [Fact]
    public void A_new_instance_reuses_immutable_copied_binaries()
    {
        byte[] key = [1, 2], binary = [3, 4, 5];
        using (var cache = new WarpProgramCache(CachePath))
        {
            cache.Store(key, binary);
            key[0] = 99;
            binary[0] = 99;
            Assert.Equal(new byte[] { 3, 4, 5 }, cache.Load([1, 2]).ToArray());
            Assert.False(File.Exists(CachePath)); // native callbacks did not touch the filesystem
        }
        using var next = new WarpProgramCache(CachePath);
        Assert.Equal(new byte[] { 3, 4, 5 }, next.Load([1, 2]).ToArray());
        Assert.True(next.Load([99, 2]).IsEmpty);
    }

    [Fact]
    public void A_recompiled_binary_replaces_the_rejected_one()
    {
        using (var cache = new WarpProgramCache(CachePath)) cache.Store([1], [2]);
        using (var cache = new WarpProgramCache(CachePath)) cache.Store([1], [3, 4]);
        using var next = new WarpProgramCache(CachePath);
        Assert.Equal(new byte[] { 3, 4 }, next.Load([1]).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_bad_or_truncated_file_is_ignored_and_repaired(bool truncate)
    {
        using (var cache = new WarpProgramCache(CachePath)) cache.Store([1], [2]);
        byte[] file = File.ReadAllBytes(CachePath);
        if (truncate) Array.Resize(ref file, file.Length - 1);
        else file[0] ^= 0xFF;
        File.WriteAllBytes(CachePath, file);
        using (var cache = new WarpProgramCache(CachePath))
        {
            Assert.True(cache.Load([1]).IsEmpty);
            cache.Store([1], [5]);
        }
        using var next = new WarpProgramCache(CachePath);
        Assert.Equal(new byte[] { 5 }, next.Load([1]).ToArray());
    }

    [Fact]
    public void New_environment_keys_miss_and_old_entries_are_bounded()
    {
        using (var cache = new WarpProgramCache(CachePath))
        {
            cache.Store(BitConverter.GetBytes(0), [1]);
            Assert.True(cache.Load(BitConverter.GetBytes(1)).IsEmpty);
            for (int i = 1; i <= WarpProgramCache.MaxEntries; i++) cache.Store(BitConverter.GetBytes(i), [2]);
            Assert.True(cache.Load(BitConverter.GetBytes(0)).IsEmpty);
        }
        using var next = new WarpProgramCache(CachePath);
        Assert.Equal(new byte[] { 2 }, next.Load(BitConverter.GetBytes(WarpProgramCache.MaxEntries)).ToArray());
    }

    [Fact]
    public void Payload_growth_is_bounded_as_well_as_entry_count()
    {
        using (var cache = new WarpProgramCache(CachePath))
        {
            byte[] binary = new byte[WarpProgramCache.MaxBinaryBytes];
            for (int i = 0; i < 6; i++) cache.Store(BitConverter.GetBytes(i), binary);
            Assert.True(cache.Load(BitConverter.GetBytes(0)).IsEmpty);
            Assert.False(cache.Load(BitConverter.GetBytes(5)).IsEmpty);
        }
        Assert.True(new FileInfo(CachePath).Length <= 4 * 1024 * 1024);
    }

    [Fact]
    public void Oversized_binaries_and_an_unwritable_location_do_not_break_rendering()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "not-a-folder"), "block directory creation");
        using var cache = new WarpProgramCache(Path.Combine(_folder, "not-a-folder", "warp.bin"));
        cache.Store([1], new byte[WarpProgramCache.MaxBinaryBytes + 1]);
        Assert.True(cache.Load([1]).IsEmpty);
        cache.Store([2], [3]);
        cache.Dispose();
        Assert.Equal(new byte[] { 3 }, cache.Load([2]).ToArray());
    }

    [Fact]
    public async Task Writes_start_only_after_reveal_and_include_later_stores()
    {
        using var cache = new WarpProgramCache(CachePath);
        cache.Store([1], [2]);
        await Task.Delay(350, TestContext.Current.CancellationToken);
        Assert.False(File.Exists(CachePath));
        cache.EnablePersistence();
        cache.EnablePersistence();
        await WaitUntil(() => File.Exists(CachePath));
        cache.Store([3], [4]);
        await WaitUntil(() =>
        {
            using var saved = new WarpProgramCache(CachePath);
            return !saved.Load([3]).IsEmpty;
        });
    }

    [Fact]
    public async Task Concurrent_callbacks_and_shutdown_leave_a_complete_newest_file()
    {
        var cache = new WarpProgramCache(CachePath);
        cache.EnablePersistence();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            for (int n = 0; n < 100; n++)
            {
                cache.Store(BitConverter.GetBytes(i), BitConverter.GetBytes(n));
                Assert.False(cache.Load(BitConverter.GetBytes(i)).IsEmpty);
            }
        })));
        cache.Dispose();
        using var next = new WarpProgramCache(CachePath);
        for (int i = 0; i < 8; i++) Assert.Equal(99, BitConverter.ToInt32(next.Load(BitConverter.GetBytes(i)).Span));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 150; i++)
        {
            if (condition()) return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.True(condition());
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
