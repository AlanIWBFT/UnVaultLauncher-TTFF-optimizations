using UnVault.Core.Install;
using UnVault.Core.Manifests;
using UnVault.Core.Util;
using UnVault.Core.Vault;

namespace UnVault.Core.Fab;

/// <summary>
/// A Fab plugin put into a project's Plugins folder rather than an engine's: it goes with the project, and the editor
/// prefers it to one of the same name in the engine. Nothing keeps track of it afterwards, as with asset packs added to
/// projects.
/// </summary>
public static class ProjectPlugins
{
    /// <summary>The plugin's own folder in a downloaded copy: Engine\Plugins\Marketplace\{name} under its data.</summary>
    public static string FolderIn(VaultEntry entry)
    {
        var manifest = Manifest.Load(entry.ManifestPath);
        FabWorkflow.RequirePlugin(manifest, entry.Title);
        return EnginePlugins.FolderOf(manifest, entry.DataDirectory)
            ?? throw new InstallException($"{entry.Title} has more than one plugin folder, so it can't be put into a project.");
    }

    /// <summary>
    /// Where it goes: {project}\Plugins\{the plugin's name}, from its .uplugin, as plugins in projects are named. Its folder
    /// in a download can be named after the Fab artifact instead; a build's folder after nothing.
    /// </summary>
    public static string TargetFor(string pluginFolder, string projectDirectory)
    {
        string? plugin = Directory.Exists(pluginFolder)
            ? Directory.EnumerateFiles(pluginFolder, "*.uplugin").Select(Path.GetFileNameWithoutExtension).FirstOrDefault()
            : null;
        string name = FileNames.IsPlain(plugin) ? plugin! : Path.GetFileName(Path.TrimEndingDirectorySeparator(pluginFolder));
        return Path.Combine(projectDirectory, "Plugins", name);
    }

    /// <summary>
    /// Copies a plugin folder (a downloaded copy's, or one just compiled) into the project, all but what a build leaves
    /// behind (Intermediate, and the HostProject it builds in). One already there is left alone: it may be another
    /// version, or changed by hand.
    /// </summary>
    /// <returns>Where it went, and how many files.</returns>
    public static async Task<(string Folder, int Files)> InstallAsync(string pluginFolder, string projectDirectory, InstallStatus status, CancellationToken cancellationToken)
    {
        string target = TargetFor(pluginFolder, projectDirectory);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new InstallException($"{target} is already there. Remove it first to install this one.");

        var source = new DirectoryInfo(Path.GetFullPath(pluginFolder));
        var files = source.EnumerateFiles("*", EnginePlugins.AllFiles)
            .Select(file => (File: file, Relative: Path.GetRelativePath(source.FullName, file.FullName)))
            .Where(f => !IsBuildLeftover(f.Relative))
            .Select(f => new FabWorkflow.FileCopy(f.File.FullName, f.Relative, f.File.Length))
            .ToList();
        if (files.Count == 0)
            throw new InstallException($"{source.FullName} has no files to install.");

        await FabWorkflow.CopyAsync(files, target, status, cancellationToken);
        return (target, files.Count);
    }

    private static bool IsBuildLeftover(string relative) =>
        relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] is var top
        && (top.Equals("Intermediate", StringComparison.OrdinalIgnoreCase) || top.Equals("HostProject", StringComparison.OrdinalIgnoreCase));
}
