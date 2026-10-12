using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using UnVault.Core.Install;
using UnVault.Core.Util;

namespace UnVault.Core.Fab;

/// <summary>
/// Compiles a plugin with an installed engine, the way its makers build it for Fab (the engine's RunUAT BuildPlugin): so it
/// loads in an engine its binaries weren't built for. Needs Visual Studio with C++ (<see cref="VisualStudio"/>).
/// </summary>
public static partial class PluginBuilder
{
    public static string RunUATPath(string engineDirectory) => Path.Combine(engineDirectory, "Engine", "Build", "BatchFiles", "RunUAT.bat");

    public static bool CanBuildWith(string engineDirectory) => File.Exists(RunUATPath(engineDirectory));

    /// <summary>
    /// Where a build's log goes: the latest for each plugin and engine. The names come from Epic's and EGL's files, so one
    /// that wouldn't make a plain file name (a "..\" in it) shares one log instead.
    /// </summary>
    public static string LogPath(string artifactID, string engineAppName)
    {
        string name = $"build-{artifactID}_{engineAppName}.log";
        return Path.Combine(AppPaths.LogsDirectory, FileNames.IsPlain(name) ? name : "build.log");
    }

    /// <summary>
    /// Builds the plugin in <paramref name="pluginFolder"/> into <paramref name="outputFolder"/> (replacing what's there),
    /// writing everything the build prints to <paramref name="logPath"/>. Cancelling stops the build and all it started.
    /// </summary>
    /// <param name="progress">Steps done and to do in the part being built, as the build counts them.</param>
    public static async Task BuildAsync(string engineDirectory, string pluginFolder, string outputFolder, string logPath,
        Action<int, int>? progress, CancellationToken cancellationToken)
    {
        string uplugin = Directory.EnumerateFiles(pluginFolder, "*.uplugin").FirstOrDefault()
            ?? throw new InstallException($"{pluginFolder} has no .uplugin file to build.");
        string runUAT = RunUATPath(engineDirectory);
        outputFolder = Path.TrimEndingDirectorySeparator(outputFolder); // a \ before the closing quote would escape it
        // A .bat runs through cmd, which takes & ^ ( ) outside quotes as part of a command, and % or ! even inside them. The
        // names come from the plugin's files: every path is quoted, and one that cmd would still change is refused.
        foreach (string path in new[] { runUAT, uplugin, outputFolder })
        {
            if (path.IndexOfAny(['%', '!', '"']) >= 0)
                throw new InstallException($"{path} can't be passed to the engine's build tools: they'd read the % or ! in it as part of a command.");
        }
        if (Directory.Exists(outputFolder))
            Directory.Delete(outputFolder, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var start = new ProcessStartInfo(runUAT)
        {
            Arguments = $"BuildPlugin \"-Plugin={uplugin}\" \"-Package={outputFolder}\" -TargetPlatforms=Win64",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(runUAT)!,
        };

        await using var log = new StreamWriter(logPath, append: false);
        var gate = new object();
        string? firstError = null;
        bool tooLong = false;
        void Read(string? line)
        {
            if (line is null)
                return;
            lock (gate)
            {
                log.WriteLine(line);
                if (firstError is null && IsError(line))
                    firstError = line.Trim();
                tooLong |= line.Contains("longer than 260 characters", StringComparison.OrdinalIgnoreCase);
            }
            if (Step(line) is { } step)
                progress?.Invoke(step.Done, step.Total);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InstallException($"Couldn't start {runUAT}.");
        }
        catch (Win32Exception ex)
        {
            throw new InstallException($"Couldn't start {runUAT}: {ex.Message}");
        }
        using (process)
        {
            process.OutputDataReceived += (_, e) => Read(e.Data);
            process.ErrorDataReceived += (_, e) => Read(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using (cancellationToken.Register(() => Stop(process)))
                await process.WaitForExitAsync(CancellationToken.None); // after a stop too, so the log is complete
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                // The build tools blame the engine's folder for long paths, but here it's the plugin's own names that are long.
                throw new InstallException(tooLong
                    ? $"The plugin's files are named too long to build here: Windows allows 260 characters in a path. Everything the build printed is in {logPath}."
                    : firstError is null
                    ? $"The build stopped (exit code {process.ExitCode}). Everything it printed is in {logPath}."
                    : $"The build stopped: {firstError} Everything it printed is in {logPath}.");
            }
        }
    }

    /// <summary>
    /// Where builds go: the app's data folder, with names kept short (temp\{4 characters}). The compiler's tools refuse paths
    /// over 260 characters, and a plugin's build nests deep in its folder
    /// (HostProject\Plugins\{name}\Intermediate\Build\Win64\x64\UnrealEditor\Development\{module}\{file}). Tests use their own.
    /// </summary>
    internal static IReadOnlyList<string> BuildRoots
    {
        get => _buildRoots ?? [Path.Combine(AppPaths.DataDirectory, "temp")];
        set => _buildRoots = value;
    }

    private static IReadOnlyList<string>? _buildRoots;

    /// <summary>The build folders given since the app started: ones a build may still be using, which a clear-up leaves.</summary>
    private static readonly ConcurrentDictionary<string, byte> Given = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A new folder to build in, under the first of <see cref="BuildRoots"/> that takes one.</summary>
    public static string NewBuildFolder()
    {
        foreach (string root in BuildRoots)
        {
            string? folder = null;
            try
            {
                // Short names, so another build may have the same one. Taken before it's made, so a clear-up never sees it untaken.
                do
                    folder = Path.GetFullPath(Path.Combine(root, Guid.NewGuid().ToString("N")[..4]));
                while (Directory.Exists(folder) || !Given.TryAdd(folder, 0));
                return Directory.CreateDirectory(folder).FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (folder is not null)
                    Given.TryRemove(folder, out _);
                // The next one.
            }
        }
        throw new InstallException("Couldn't make a folder to build in.");
    }

    /// <summary>Deletes a finished build, and its root once nothing else is in it. Anything else is left alone.</summary>
    public static void DeleteBuild(string buildFolder)
    {
        if (!IsBuildFolder(buildFolder))
            return;
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(buildFolder));
        try
        {
            if (Directory.Exists(full))
                Directory.Delete(full, recursive: true);
            string root = Path.GetDirectoryName(full)!;
            if (!Directory.EnumerateFileSystemEntries(root).Any())
                Directory.Delete(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleared at the next start.
        }
        finally
        {
            Given.TryRemove(full, out _);
        }
    }

    /// <summary>
    /// Deletes what earlier builds left (one the app's closing cut short): only the folders builds were given, and none
    /// given since the app started, as a build may be in one.
    /// </summary>
    public static void ClearBuilds()
    {
        foreach (string root in BuildRoots)
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (string folder in Directory.GetDirectories(root)) // as they are now: one made meanwhile is in use
                {
                    if (!Given.ContainsKey(Path.GetFullPath(folder)))
                        DeleteBuild(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Next time.
            }
        }
    }

    /// <summary>One of the folders <see cref="NewBuildFolder"/> gives, directly in one of the roots: nothing else is deleted.</summary>
    private static bool IsBuildFolder(string folder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return BuildFolderName().IsMatch(Path.GetFileName(full))
            && BuildRoots.Any(root => string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase));
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true); // the compilers it started too
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Ended already.
        }
    }

    /// <summary>"[12/48] Compile Module.Foo.cpp": the build's own count of its steps.</summary>
    internal static (int Done, int Total)? Step(string line) =>
        StepPattern().Match(line) is { Success: true } match ? (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) : null;

    /// <summary>A compiler's "Foo.cpp(12): error C2065: …", or the build tools' own "ERROR: …".</summary>
    internal static bool IsError(string line) => ErrorPattern().IsMatch(line);

    [GeneratedRegex(@"^\s*\[(\d+)/(\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex StepPattern();

    [GeneratedRegex("^[0-9a-f]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildFolderName();

    [GeneratedRegex(@":\s*(fatal\s+)?error\b|^\s*ERROR:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ErrorPattern();
}

/// <summary>Visual Studio, or its Build Tools, with the C++ compiler: what compiling a plugin needs.</summary>
public static class VisualStudio
{
    /// <summary>Asks Visual Studio's own installer tool (vswhere), which comes with every edition since 2017.</summary>
    public static bool HasCppCompiler()
    {
        string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
            return false;
        try
        {
            var start = new ProcessStartInfo(vswhere) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (string argument in new[] { "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath", "-nologo" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null)
                return false;
            string found = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return found.Trim().Length > 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
