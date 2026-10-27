#:property PublishAot=false
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

const string revision = "8eeda4f6f546165b3f72e63c9f42247abb306905";
const string version = "12.1.3.3";
string scriptDirectory = Path.GetDirectoryName(SourceFile())!;
string root = Path.GetFullPath(Path.Combine(scriptDirectory, "../.."));
// Keep each patch revision in its own checkout instead of rewriting an older, possibly modified source tree.
string source = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(root, "Bin", "dependencies", $"Avalonia-{version}");
string patch = Path.Combine(scriptDirectory, "deferred-d3d11.patch");
string feed = Path.Combine(root, "Bin", "packages");
Directory.CreateDirectory(Path.GetDirectoryName(source)!);
Directory.CreateDirectory(feed);

if (!Directory.Exists(source))
    Run("gh", "repo", "clone", "AvaloniaUI/Avalonia", source, "--", "--branch", "12.1.3", "--depth", "1");
if (Output("git", "-C", source, "rev-parse", "HEAD").Trim() != revision)
    throw new InvalidOperationException($"The patch requires Avalonia commit {revision}.");
if (ExitCode("git", "-C", source, "apply", "--reverse", "--check", patch) != 0)
{
    Run("git", "-C", source, "apply", "--check", patch);
    Run("git", "-C", source, "apply", patch);
}
Run("git", "-C", source, "submodule", "update", "--init", "--depth", "1", "external/XamlX");

string project = Path.Combine(source, "src", "Windows", "Avalonia.Win32", "Avalonia.Win32.csproj");
Run("dotnet", "build", project, "-c", "Release", "-p:AvsSkipBuildingLegacyTargetFrameworks=True", "-p:DisableSourceLink=true");

// Publish only the Windows backend. All framework dependencies remain the official 12.1.3 packages.
string nuspec = Path.Combine(feed, "Avalonia.Win32.nuspec");
var files = new List<XElement>();
foreach (string assembly in new[] { "Avalonia.Win32", "Avalonia.Win32.Automation" })
    foreach (string extension in new[] { "dll", "pdb", "xml" })
    {
        string file = Path.Combine(source, "src", "Windows", assembly, "bin", "Release", "net10.0", $"{assembly}.{extension}");
        if (File.Exists(file))
            files.Add(new XElement("file", new XAttribute("src", file), new XAttribute("target", "lib/net10.0")));
    }
new XDocument(new XElement("package",
    new XElement("metadata",
        new XElement("id", "Avalonia.Win32"), new XElement("version", version),
        new XElement("authors", "Avalonia Team; UnVault contributors"),
        new XElement("license", new XAttribute("type", "expression"), "MIT"),
        new XElement("description", "Avalonia 12.1.3 Windows backend with deferred hardware rendering, first-frame synchronization and optional WARP program caching for UnVault."),
        new XElement("dependencies", new XElement("group", new XAttribute("targetFramework", "net10.0"),
            new XElement("dependency", new XAttribute("id", "Avalonia"), new XAttribute("version", "[12.1.3]")),
            new XElement("dependency", new XAttribute("id", "Avalonia.Angle.Windows.Natives"), new XAttribute("version", "[2.1.27548.20260419]"))))),
    new XElement("files", files))).Save(nuspec);
Run("dotnet", "pack", project, "--no-build", "-c", "Release", "-p:AvsSkipBuildingLegacyTargetFrameworks=True",
    "-p:DisableSourceLink=true", $"-p:NuspecFile={nuspec}", "-o", feed);
Console.WriteLine($"Patched backend: {Path.Combine(feed, $"Avalonia.Win32.{version}.nupkg")}");

static string SourceFile([CallerFilePath] string file = "") => file;

static ProcessStartInfo Command(string executable, string[] arguments, bool capture = false)
{
    var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = capture, RedirectStandardError = capture };
    foreach (string argument in arguments)
        info.ArgumentList.Add(argument);
    return info;
}

static void Run(string executable, params string[] arguments)
{
    using var process = Process.Start(Command(executable, arguments))!;
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{executable} {string.Join(' ', arguments)} failed ({process.ExitCode}).");
}

static string Output(string executable, params string[] arguments)
{
    using var process = Process.Start(Command(executable, arguments, true))!;
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    Task.WaitAll(output, error);
    if (process.ExitCode != 0)
        throw new InvalidOperationException(error.GetAwaiter().GetResult());
    return output.GetAwaiter().GetResult();
}

static int ExitCode(string executable, params string[] arguments)
{
    using var process = Process.Start(Command(executable, arguments, true))!;
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    Task.WaitAll(output, error);
    return process.ExitCode;
}
