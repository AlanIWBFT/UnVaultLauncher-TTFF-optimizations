using UnVault.Core.Fab;
using UnVault.Core.Install;
using UnVault.Core.Vault;

namespace UnVault.Core.Tests;

/// <summary>A Fab plugin put into a project's Plugins folder, compiled first with an engine when asked.</summary>
public sealed class ProjectPluginTests : IDisposable
{
    // A space in the path, as in "E:\Epic Games": the build's arguments must keep it.
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"unvault-project plugins-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string Write(string relative, string content = "x")
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void The_plugin_folder_is_found_in_a_downloaded_copy()
    {
        var entry = new VaultEntry { ArtifactID = "Cliffside0a1b2c3dV7", Title = "Cliffside!", Directory = Path.Combine(_dir, "vault", "Cliffside0a1b2c3dV7") };
        Write(Path.Combine("vault", "Cliffside0a1b2c3dV7", "manifest"), JSONManifestBuilder.Build("Cliffside0a1b2c3dV7", "5.6.0-1",
            ("Engine/Plugins/Marketplace/Cliffside/Cliffside.uplugin", 10), ("Engine/Plugins/Marketplace/Cliffside/Binaries/Win64/UnrealEditor-Cliffside.dll", 20)));

        string folder = ProjectPlugins.FolderIn(entry);

        Assert.Equal(Path.Combine(entry.DataDirectory, "Engine", "Plugins", "Marketplace", "Cliffside"), folder);
        Assert.Equal(Path.Combine(_dir, "Hillside", "Plugins", "Cliffside"), ProjectPlugins.TargetFor(folder, Path.Combine(_dir, "Hillside")));
    }

    [Fact]
    public async Task It_goes_into_the_projects_Plugins_folder_without_build_leftovers_and_never_over_one_there()
    {
        Write(@"built\Cliffside\Cliffside.uplugin", "{}");
        Write(@"built\Cliffside\Binaries\Win64\UnrealEditor-Cliffside.dll");
        Write(@"built\Cliffside\Source\Cliffside\Cliffside.Build.cs");
        Write(@"built\Cliffside\Intermediate\Build\Win64\UnrealEditor\Development\Cliffside\Module.Cliffside.cpp.obj");
        string project = Path.Combine(_dir, "Hillside");
        var status = new InstallStatus();

        var (folder, files) = await ProjectPlugins.InstallAsync(Path.Combine(_dir, @"built\Cliffside"), project, status, default);

        Assert.Equal((Path.Combine(project, "Plugins", "Cliffside"), 3), (folder, files));
        Assert.Equal(
            [@"Binaries\Win64\UnrealEditor-Cliffside.dll", "Cliffside.uplugin", @"Source\Cliffside\Cliffside.Build.cs"],
            Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f)).Order());
        Assert.Equal((3, 3), (status.FilesDone, status.FilesTotal));

        // Another version, or one changed by hand: left as it is.
        var again = await Assert.ThrowsAsync<InstallException>(() => ProjectPlugins.InstallAsync(Path.Combine(_dir, @"built\Cliffside"), project, new InstallStatus(), default));
        Assert.Contains("already there", again.Message);
    }

    /// <summary>
    /// In a project it's named after its .uplugin, as plugins there are: its folder in a download can be named after the
    /// Fab artifact, and a build's after nothing. What the build ran in stays out.
    /// </summary>
    [Fact]
    public async Task It_takes_the_plugins_own_name_in_the_project()
    {
        Write(@"0a1b2c3d\ReliefMapping.uplugin", "{}");
        Write(@"0a1b2c3d\Binaries\Win64\UnrealEditor-ReliefMapping.dll");
        Write(@"0a1b2c3d\HostProject\Plugins\ReliefMapping\ReliefMapping.uplugin", "{}");
        string project = Path.Combine(_dir, "Hillside");

        var (folder, files) = await ProjectPlugins.InstallAsync(Path.Combine(_dir, "0a1b2c3d"), project, new InstallStatus(), default);

        Assert.Equal((Path.Combine(project, "Plugins", "ReliefMapping"), 2), (folder, files));
        Assert.False(Directory.Exists(Path.Combine(folder, "HostProject")));
    }

    /// <summary>
    /// Builds go to short names in the app's data folder, as the compiler's tools refuse long paths; a root that won't take
    /// a folder hands over to the next. Only the folders builds were given are ever deleted, and none a build may be in.
    /// </summary>
    [Fact]
    public void Builds_go_to_a_short_folder_and_only_theirs_are_cleared()
    {
        var roots = PluginBuilder.BuildRoots;
        // In the tests' data folder, like the roots other tests see meanwhile (they run alongside).
        string here = Directory.CreateDirectory(Path.Combine(AppPaths.DataDirectory, $"short-builds-{Guid.NewGuid():N}")).FullName;
        try
        {
            string blocked = Path.Combine(here, "not a folder");
            File.WriteAllText(blocked, "x"); // a file where the first root would be
            string root = Path.Combine(here, "UnVaultBuild");
            string earlier = Directory.CreateDirectory(Path.Combine(root, "0f0f")).FullName; // one closing the app cut short
            PluginBuilder.BuildRoots = [blocked, root];

            string first = PluginBuilder.NewBuildFolder(), second = PluginBuilder.NewBuildFolder();
            Assert.Equal(root, Path.GetDirectoryName(first));
            Assert.Matches("^[0-9a-f]{4}$", Path.GetFileName(first));
            Assert.NotEqual(first, second);
            string keep = Directory.CreateDirectory(Path.Combine(root, "someone else's")).FullName;
            string elsewhere = Directory.CreateDirectory(Path.Combine(here, "elsewhere")).FullName;

            PluginBuilder.DeleteBuild(first);
            PluginBuilder.DeleteBuild(Path.Combine(elsewhere, "a1b2")); // named like one, but not in a root: its folder stays
            Assert.False(Directory.Exists(first));
            Assert.True(Directory.Exists(elsewhere));

            // At the start: what earlier runs left goes, but not the second, made since: a build may be in it.
            PluginBuilder.ClearBuilds();
            Assert.False(Directory.Exists(earlier));
            Assert.Equal(new[] { keep, second }.Order(), Directory.EnumerateDirectories(root).Order());

            Directory.Delete(keep);
            PluginBuilder.DeleteBuild(second);
            Assert.False(Directory.Exists(root)); // the root too, once nothing's in it
        }
        finally
        {
            PluginBuilder.BuildRoots = roots;
            Directory.Delete(here, recursive: true);
        }
    }

    [Fact]
    public void Builds_go_to_the_apps_data_folder_by_default()
    {
        var roots = PluginBuilder.BuildRoots;
        try
        {
            PluginBuilder.BuildRoots = null!; // as it is outside tests
            Assert.Equal([Path.Combine(AppPaths.DataDirectory, "temp")], PluginBuilder.BuildRoots);
        }
        finally
        {
            PluginBuilder.BuildRoots = roots;
        }
    }

    [Fact]
    public void A_builds_log_is_named_after_the_plugin_and_engine_and_stays_in_the_logs_folder()
    {
        Assert.Equal(Path.Combine(AppPaths.LogsDirectory, "build-Cliffside0a1b2c3dV7_UE_5.6.log"), PluginBuilder.LogPath("Cliffside0a1b2c3dV7", "UE_5.6"));
        Assert.Equal(Path.Combine(AppPaths.LogsDirectory, "build.log"), PluginBuilder.LogPath("Cliffside0a1b2c3dV7", @"UE_5.6\..\..\..\Elsewhere"));
    }

    [Fact]
    public void The_builds_steps_and_errors_are_read_from_what_it_prints()
    {
        Assert.Equal((12, 48), PluginBuilder.Step("[12/48] Compile Module.Cliffside.cpp"));
        Assert.Null(PluginBuilder.Step("Building UnrealEditor..."));
        Assert.True(PluginBuilder.IsError(@"E:\x\Cliffside.cpp(12): error C2065: 'Relief': undeclared identifier"));
        Assert.True(PluginBuilder.IsError("ERROR: Unable to find plugin"));
        Assert.True(PluginBuilder.IsError("Cliffside.cpp(3): fatal error C1083: Cannot open include file"));
        Assert.False(PluginBuilder.IsError("Warnings and errors from the previous build: none"));
    }

    /// <summary>An engine whose RunUAT.bat prints what a build does, then ends with <paramref name="exitCode"/>.</summary>
    private string FakeEngine(string name, int exitCode, bool hangs = false)
    {
        string engine = Path.Combine(_dir, name);
        Write(Path.Combine(name, "Engine", "Build", "BatchFiles", "RunUAT.bat"), string.Join("\r\n",
            "@echo off",
            "echo Running AutomationTool with %*",
            "echo [1/2] Compile Module.Cliffside.cpp",
            hangs ? "ping -n 60 127.0.0.1 >nul" : "echo [2/2] Link UnrealEditor-Cliffside.dll",
            exitCode == 0 ? "echo BUILD SUCCESSFUL" : @"echo E:\x\Cliffside.cpp(12): error C2065: 'Relief': undeclared identifier",
            exitCode == 0 ? "" : "echo ERROR: BUILD FAILED",
            $"exit /b {exitCode}"));
        return engine;
    }

    [Fact]
    public async Task A_build_runs_the_engines_tool_and_tells_how_it_went()
    {
        string plugin = Path.GetDirectoryName(Write(@"vault data\Cliffside\Cliffside.uplugin", "{}"))!;
        string output = Path.Combine(_dir, @"builds\Cliffside"), log = Path.Combine(_dir, @"logs\build.log");
        var steps = new List<(int, int)>();
        Assert.True(PluginBuilder.CanBuildWith(FakeEngine("UE_5.6", 0)));

        await PluginBuilder.BuildAsync(Path.Combine(_dir, "UE_5.6"), plugin, output, log, (done, total) => { lock (steps) steps.Add((done, total)); }, default);

        string printed = File.ReadAllText(log);
        Assert.Contains($"BuildPlugin \"-Plugin={Path.Combine(plugin, "Cliffside.uplugin")}\" \"-Package={output}\" -TargetPlatforms=Win64", printed);
        Assert.Contains("BUILD SUCCESSFUL", printed);
        Assert.Equal([(1, 2), (2, 2)], steps);

        // A failed one names the first error, and where the rest is.
        var failed = await Assert.ThrowsAsync<InstallException>(() =>
            PluginBuilder.BuildAsync(FakeEngine("UE_5.7", 6), plugin, output, log, null, default));
        Assert.StartsWith(@"The build stopped: E:\x\Cliffside.cpp(12): error C2065", failed.Message);
        Assert.EndsWith($"Everything it printed is in {log}.", failed.Message);

        // Paths too long: the build tools blame the engine's folder, but it's the plugin's names.
        string tooLong = Path.Combine(_dir, "UE_5.4");
        Write(Path.Combine("UE_5.4", "Engine", "Build", "BatchFiles", "RunUAT.bat"), string.Join("\r\n",
            "@echo off",
            "echo The following action paths are longer than 260 characters. Please move the engine to a directory with a shorter path.",
            "exit /b 6"));
        var named = await Assert.ThrowsAsync<InstallException>(() => PluginBuilder.BuildAsync(tooLong, plugin, output, log, null, default));
        Assert.StartsWith("The plugin's files are named too long to build here", named.Message);
    }

    /// <summary>
    /// The engine's tool is a .bat, run through cmd: a name with &amp; in it mustn't end the command and start another, and
    /// one that cmd would change even in quotes (% or !) is refused.
    /// </summary>
    [Fact]
    public async Task A_plugins_name_never_becomes_part_of_a_command()
    {
        // No space in it, so nothing is quoted unless the build quotes it.
        string dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"unvault-names-{Guid.NewGuid():N}")).FullName;
        try
        {
            string engine = Path.Combine(dir, "UE_5.6"), runUAT = PluginBuilder.RunUATPath(engine);
            Directory.CreateDirectory(Path.GetDirectoryName(runUAT)!);
            File.WriteAllText(runUAT, "@echo off\r\necho %2\r\n");
            string plugin = Directory.CreateDirectory(Path.Combine(dir, "Rock&echo,INJECTED&")).FullName;
            File.WriteAllText(Path.Combine(plugin, "Rock.uplugin"), "{}");
            string log = Path.Combine(dir, "build.log");

            await PluginBuilder.BuildAsync(engine, plugin, Path.Combine(dir, "a1b2"), log, null, default);

            Assert.Equal([$"\"-Plugin={Path.Combine(plugin, "Rock.uplugin")}\""], File.ReadAllLines(log));

            string odd = Directory.CreateDirectory(Path.Combine(dir, "Odd%PATH%")).FullName;
            File.WriteAllText(Path.Combine(odd, "Odd.uplugin"), "{}");
            var refused = await Assert.ThrowsAsync<InstallException>(() => PluginBuilder.BuildAsync(engine, odd, Path.Combine(dir, "a1b2"), log, null, default));
            Assert.Contains("% or !", refused.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Pausing_stops_the_build()
    {
        string plugin = Path.GetDirectoryName(Write(@"vault data\Cliffside\Cliffside.uplugin", "{}"))!;
        using var pause = new CancellationTokenSource();
        var started = DateTime.UtcNow;

        // Paused once it's on its first step; the build would go on for a long while.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PluginBuilder.BuildAsync(FakeEngine("UE_5.5", 0, hangs: true), plugin,
            Path.Combine(_dir, "builds"), Path.Combine(_dir, "build.log"), (_, _) => pause.Cancel(), pause.Token));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30), "it stopped, rather than ran to its end");
    }
}
