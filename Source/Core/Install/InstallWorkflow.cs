using System.Text.RegularExpressions;
using UnVault.Core.EGL;
using UnVault.Core.Epic;
using UnVault.Core.Manifests;

namespace UnVault.Core.Install;

/// <summary>A build to install: its manifest and download locations, plus catalog identity for registration.</summary>
public sealed record InstallSource(DownloadedManifest Downloaded, string CatalogNamespace, string CatalogItemID)
{
    public Manifest Manifest => Downloaded.Manifest;
}

/// <summary>What a modify would do: the new selection, files to delete, and files to download.</summary>
public sealed record ModifyPlan(ExistingInstall Install, IReadOnlySet<string> NewTags, IReadOnlyList<FileManifest> ToRemove, InstallPlan? ToAdd)
{
    public bool IsEmpty => ToRemove.Count == 0 && ToAdd is null;
    public long BytesFreed => ToRemove.Sum(f => f.FileSize);
}

/// <summary>
/// What an update would do: the new build, the components kept, the files to download (new or changed) and those to delete
/// (gone from the new build).
/// </summary>
public sealed record UpdatePlan(ExistingInstall Install, InstallSource Source, IReadOnlySet<string> Tags, InstallPlan Write, IReadOnlyList<FileManifest> Delete);

/// <summary>The install/modify steps shared by the CLI and the GUI.</summary>
public static class InstallWorkflow
{
    /// <summary>The latest Live build of an app the account owns.</summary>
    public static async Task<InstallSource> FetchLatestAsync(EpicAPIClient api, string appName, CancellationToken cancellationToken)
    {
        var assets = await api.GetAssetsAsync(cancellationToken: cancellationToken);
        var asset = assets.FirstOrDefault(a => string.Equals(a.AppName, appName, StringComparison.OrdinalIgnoreCase))
            ?? throw new EpicAPIException($"Your account doesn't own {appName}.");
        return await FetchAsync(api, asset, cancellationToken);
    }

    public static async Task<InstallSource> FetchAsync(EpicAPIClient api, EpicAsset asset, CancellationToken cancellationToken)
    {
        var build = await api.GetBuildInfoAsync(asset.Namespace, asset.CatalogItemID, asset.AppName, cancellationToken: cancellationToken);
        var downloaded = await api.DownloadManifestAsync(build, cancellationToken);
        return new InstallSource(downloaded, asset.Namespace, asset.CatalogItemID);
    }

    /// <summary>A local manifest file; chunk locations come from <paramref name="baseURLs"/> or EGL's record of the same app.</summary>
    public static InstallSource FromLocalManifest(byte[] raw, IReadOnlyList<string> baseURLs)
    {
        var manifest = Manifest.Parse(raw);
        var item = EGLInstallations.ReadItems().FirstOrDefault(i => string.Equals(i.AppName, manifest.Meta.AppName, StringComparison.OrdinalIgnoreCase));
        var urls = baseURLs.Count > 0 ? baseURLs : item?.BaseURLs ?? [];
        if (urls.Count == 0)
            throw new InstallException("No download location is known for this build; pass a CloudDir base URL.");

        var sources = urls.Select(u => new ChunkSource(u.TrimEnd('/'))).ToList();
        return new InstallSource(new DownloadedManifest(manifest, raw, sources, new Dictionary<string, string>()),
            item?.CatalogNamespace ?? "", item?.CatalogItemID ?? "");
    }

    public static InstallPlan PlanInstall(InstallSource source, IReadOnlySet<string> tags, Regex? only = null)
    {
        var unknown = tags.Except(source.Manifest.GetInstallTags()).ToList();
        if (unknown.Count > 0)
            throw new InstallException($"Unknown component(s): {string.Join(", ", unknown)}.");
        return InstallPlan.Create(source.Manifest, source.Manifest.SelectFiles(tags).Where(f => only is null || only.IsMatch(f.Filename)));
    }

    /// <summary>
    /// Downloads the plan into <paramref name="directory"/>, then records the install and (for complete engine
    /// installs) registers it in LauncherInstalled.dat so UE tools find it. Re-running resumes.
    /// </summary>
    public static async Task InstallAsync(
        InstallSource source, InstallPlan plan, IReadOnlySet<string> tags, string? fileFilter, string directory,
        Installer installer, InstallStatus status, bool register, CancellationToken cancellationToken)
    {
        await installer.InstallAsync(plan, directory, source.Downloaded.Sources, source.Downloaded.Secrets, status, cancellationToken);

        var manifest = source.Manifest;
        new InstallRecord
        {
            AppName = manifest.Meta.AppName,
            BuildVersion = manifest.Meta.BuildVersion,
            CatalogNamespace = source.CatalogNamespace,
            CatalogItemID = source.CatalogItemID,
            InstallTags = [.. tags.Order(StringComparer.Ordinal)],
            InstallSize = plan.InstallBytes,
            FileFilter = fileFilter,
            BaseURLs = [.. source.Downloaded.Sources.Where(s => s.Query.Length == 0).Select(s => s.BaseURL)],
            InstalledAt = DateTimeOffset.Now,
        }.Save(directory, source.Downloaded.RawBytes);

        // Partial installs aren't usable engines, so UE tools shouldn't be pointed at them.
        if (register && fileFilter is null && IsEngine(manifest))
        {
            EGLInstallations.RegisterLauncherInstall(new LauncherInstalledEntry
            {
                InstallLocation = directory,
                NamespaceID = source.CatalogNamespace.Length > 0 ? source.CatalogNamespace : "ue",
                ItemID = source.CatalogItemID,
                ArtifactID = manifest.Meta.AppName,
                AppVersion = manifest.Meta.BuildVersion,
                AppName = manifest.Meta.AppName,
            });
        }
    }

    public static bool IsEngine(Manifest manifest) => manifest.Meta.AppName.StartsWith("UE_", StringComparison.Ordinal);

    public static ModifyPlan PlanModify(ExistingInstall install, IReadOnlySet<string> newTags)
    {
        var unknown = newTags.Except(install.Manifest.GetInstallTags()).ToList();
        if (unknown.Count > 0)
            throw new InstallException($"Unknown component(s): {string.Join(", ", unknown)}.");

        var currentFiles = install.SelectFiles(null).ToHashSet();
        var newFiles = install.SelectFiles(newTags).ToHashSet();
        var toRemove = currentFiles.Where(f => !newFiles.Contains(f)).ToList();
        var toAdd = newFiles.Where(f => !currentFiles.Contains(f)).ToList();
        return new ModifyPlan(install, newTags, toRemove, toAdd.Count > 0 ? InstallPlan.Create(install.Manifest, toAdd) : null);
    }

    /// <summary>
    /// Applies a modify: deletes deselected files, then downloads added ones. The record follows the files: removed
    /// components leave it before their files are deleted, added ones join it once their files are complete. So after a
    /// cancel or a failure, running the same modify again resumes the download instead of finding nothing to change.
    /// </summary>
    public static async Task<InstallCleaner.Result> ApplyModifyAsync(ModifyPlan plan, Installer installer, InstallStatus status, CancellationToken cancellationToken)
    {
        var install = plan.Install;
        if (plan.ToAdd is not null && install.Sources.Count == 0)
            throw new InstallException("Can't add components: no download location is known for this install.");
        byte[] rawManifest = await File.ReadAllBytesAsync(install.ManifestPath, cancellationToken);

        // Recorded before any file changes, also for an install EGL made, which has no record yet: without one, components
        // are judged by the files on disk, where an added one stopped halfway would pass for installed.
        if (plan.ToRemove.Count > 0 || install.Record is null)
        {
            var kept = install.InstallTags.Where(plan.NewTags.Contains).ToHashSet(StringComparer.Ordinal);
            install.ToRecord(kept).Save(install.Directory, rawManifest);
        }

        InstallCleaner.Result cleaned = default;
        if (plan.ToRemove.Count > 0)
            cleaned = await Task.Run(() => InstallCleaner.DeleteFiles(install.Directory, plan.ToRemove, cancellationToken), cancellationToken);

        if (plan.ToAdd is not null)
            await installer.InstallAsync(plan.ToAdd, install.Directory, install.Sources, new Dictionary<string, string>(), status, cancellationToken);

        install.ToRecord(plan.NewTags).Save(install.Directory, rawManifest);
        return cleaned;
    }

    /// <summary>
    /// How to bring an install up to the build in <paramref name="source"/>, as the Epic Games Launcher updates: with the
    /// same components (those the new build still has), downloading only the files that are new or changed, and deleting
    /// those it no longer has.
    /// </summary>
    public static UpdatePlan PlanUpdate(ExistingInstall install, InstallSource source)
    {
        var manifest = source.Manifest;
        if (!string.Equals(manifest.Meta.AppName, install.Manifest.Meta.AppName, StringComparison.OrdinalIgnoreCase))
            throw new InstallException($"{manifest.Meta.AppName} isn't a build of {install.Manifest.Meta.AppName}.");

        var offered = manifest.GetInstallTags().ToHashSet(StringComparer.Ordinal);
        var tags = install.InstallTags.Where(offered.Contains).ToHashSet(StringComparer.Ordinal);
        var after = manifest.SelectFiles(tags).Where(f => install.InstalledFilter is null || install.InstalledFilter.IsMatch(f.Filename));
        var (write, delete) = Changes(install.SelectFiles(null), after);
        return new UpdatePlan(install, source, tags, InstallPlan.Create(manifest, write), delete);
    }

    /// <summary>
    /// Applies an update: the new and changed files are written over the old ones, then the files the new build dropped are
    /// deleted, and the new build is recorded. The record names the old build until it's all done, so an update stopped
    /// partway is planned the same again, and resumes where it stopped.
    /// </summary>
    /// <param name="launcherInstalledPath">Tests keep their own LauncherInstalled.dat.</param>
    public static async Task<InstallCleaner.Result> ApplyUpdateAsync(UpdatePlan plan, Installer installer, InstallStatus status, CancellationToken cancellationToken,
        string? launcherInstalledPath = null)
    {
        var (install, source) = (plan.Install, plan.Source);
        await installer.InstallAsync(plan.Write, install.Directory, source.Downloaded.Sources, source.Downloaded.Secrets, status, cancellationToken);
        var cleaned = await Task.Run(() => InstallCleaner.DeleteFiles(install.Directory, plan.Delete, cancellationToken), cancellationToken);

        var manifest = source.Manifest;
        new InstallRecord
        {
            AppName = manifest.Meta.AppName,
            BuildVersion = manifest.Meta.BuildVersion,
            CatalogNamespace = source.CatalogNamespace.Length > 0 ? source.CatalogNamespace : install.Record?.CatalogNamespace ?? "",
            CatalogItemID = source.CatalogItemID.Length > 0 ? source.CatalogItemID : install.Record?.CatalogItemID ?? "",
            InstallTags = [.. plan.Tags.Order(StringComparer.Ordinal)],
            InstallSize = manifest.SelectFiles(plan.Tags).Where(f => install.InstalledFilter is null || install.InstalledFilter.IsMatch(f.Filename)).Sum(f => f.FileSize),
            AdoptedFromEGL = install.Record?.AdoptedFromEGL ?? install.EGLItem is not null,
            FileFilter = install.Record?.FileFilter,
            BaseURLs = [.. source.Downloaded.Sources.Where(s => s.Query.Length == 0).Select(s => s.BaseURL)],
            InstalledAt = install.Record?.InstalledAt ?? DateTimeOffset.Now,
        }.Save(install.Directory, source.Downloaded.RawBytes);

        // UE's tools read the version from here too.
        if (install.InstalledFilter is null && IsEngine(manifest))
        {
            EGLInstallations.RegisterLauncherInstall(new LauncherInstalledEntry
            {
                InstallLocation = install.Directory,
                NamespaceID = source.CatalogNamespace.Length > 0 ? source.CatalogNamespace : "ue",
                ItemID = source.CatalogItemID,
                ArtifactID = manifest.Meta.AppName,
                AppVersion = manifest.Meta.BuildVersion,
                AppName = manifest.Meta.AppName,
            }, launcherInstalledPath);
        }
        return cleaned;
    }

    /// <summary>
    /// What going from one set of files to another takes: the files to write (new, or changed) and those to delete (gone).
    /// A file only counts as unchanged when both sides have its hash and they match.
    /// </summary>
    internal static (List<FileManifest> Write, List<FileManifest> Delete) Changes(IEnumerable<FileManifest> before, IEnumerable<FileManifest> after)
    {
        var old = before.GroupBy(f => f.Filename, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var current = after.ToList();
        var write = current.Where(f => !old.TryGetValue(f.Filename, out var o) || !SameContent(o, f)).ToList();
        var now = current.Select(f => f.Filename).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var delete = old.Values.Where(f => !now.Contains(f.Filename)).ToList();
        return (write, delete);

        static bool SameContent(FileManifest a, FileManifest b) =>
            a.FileSize == b.FileSize && a.SHA1.Length == 20 && a.SHA1.AsSpan().SequenceEqual(b.SHA1);
    }
}
