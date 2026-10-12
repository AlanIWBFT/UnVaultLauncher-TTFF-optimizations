using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using UnVault.Core.Install;

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
    /// Builds the plugin in <paramref name="pluginFolder"/> into <paramref name="outputFolder"/> (replacing what's there),
    /// writing everything the build prints to <paramref name="logPath"/>. Cancelling stops the build and all it started.
    /// </summary>
    /// <param name="progress">Steps done and to do in the part being built, as the build counts them.</param>
    public static async Task BuildAsync(string engineDirectory, string pluginFolder, string outputFolder, string logPath,
        Action<int, int>? progress, CancellationToken cancellationToken)
    {
        string uplugin = Directory.EnumerateFiles(pluginFolder, "*.uplugin").FirstOrDefault()
            ?? throw new InstallException($"{pluginFolder} has no .uplugin file to build.");
        if (Directory.Exists(outputFolder))
            Directory.Delete(outputFolder, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        string runUAT = RunUATPath(engineDirectory);
        var start = new ProcessStartInfo(runUAT)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(runUAT)!,
        };
        foreach (string argument in new[] { "BuildPlugin", $"-Plugin={uplugin}", $"-Package={outputFolder}", "-TargetPlatforms=Win64" })
            start.ArgumentList.Add(argument);

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
    /// Where builds go: the app's data folder, with names kept short (b\{4 characters}). The compiler's tools refuse paths
    /// over 260 characters, and a plugin's build nests deep in its folder
    /// (HostProject\Plugins\{name}\Intermediate\Build\Win64\x64\UnrealEditor\Development\{module}\{file}). Tests use their own.
    /// </summary>
    internal static IReadOnlyList<string> BuildRoots
    {
        get => _buildRoots ?? [Path.Combine(AppPaths.DataDirectory, "b")];
        set => _buildRoots = value;
    }

    private static IReadOnlyList<string>? _buildRoots;

    /// <summary>A new folder to build in, under the first of <see cref="BuildRoots"/> that takes one.</summary>
    public static string NewBuildFolder()
    {
        foreach (string root in BuildRoots)
        {
            try
            {
                string folder;
                do
                    folder = Path.Combine(root, Guid.NewGuid().ToString("N")[..4]);
                while (Directory.Exists(folder)); // short names, so another build may have the same one
                return Directory.CreateDirectory(folder).FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The next one.
            }
        }
        throw new InstallException("Couldn't make a folder to build in.");
    }

    /// <summary>Deletes a finished build, and its root once nothing else is in it.</summary>
    public static void DeleteBuild(string buildFolder)
    {
        try
        {
            if (IsBuildFolder(buildFolder))
                Directory.Delete(buildFolder, recursive: true);
            string root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(buildFolder))!;
            if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
                Directory.Delete(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleared at the next start.
        }
    }

    /// <summary>Deletes what earlier builds left (one the app's closing cut short): only the folders builds were given.</summary>
    public static void ClearBuilds()
    {
        foreach (string root in BuildRoots)
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (string folder in Directory.EnumerateDirectories(root))
                    DeleteBuild(folder);
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
