using UnVault.Core.Install;
using UnVault.Core.Manifests;

namespace UnVault.Core.Tests;

/// <summary>The installer a build names to run once installed, as engines name the Visual C++ runtime.</summary>
public sealed class PrerequisiteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"unvault-prereq-{Guid.NewGuid():N}");
    private readonly string _outside = Path.Combine(Path.GetTempPath(), $"unvault-prereq-outside-{Guid.NewGuid():N}.exe");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        File.Delete(_outside);
    }

    private static Manifest Naming(string path, string arguments = "") => new()
    {
        Version = 21,
        Meta = new ManifestMeta { FeatureLevel = 21, AppName = "UE_9.9", BuildVersion = "9.9.0-1", PrereqPath = path, PrereqArgs = arguments },
        Chunks = [],
        Files = [],
        CustomFields = new Dictionary<string, string>(),
    };

    [Fact]
    public void Found_where_the_build_says_once_the_install_has_it()
    {
        const string Redist = "Engine/Extras/Redist/en-us/vc_redist.x64.exe";
        var manifest = Naming(Redist, "/quiet");
        Assert.Null(Prerequisite.Find(manifest, _dir)); // not installed yet

        string installer = Path.Combine(_dir, "Engine", "Extras", "Redist", "en-us", "vc_redist.x64.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(installer)!);
        File.WriteAllBytes(installer, [0]);
        Assert.Equal(new Prerequisite(installer, "/quiet"), Prerequisite.Find(manifest, _dir));

        // 4.27's names no arguments: its installer shows its own window.
        Assert.Equal("", Prerequisite.Find(Naming(Redist), _dir)!.Arguments);

        // None named, or one outside the install: nothing to run.
        Assert.Null(Prerequisite.Find(Naming(""), _dir));
        File.WriteAllBytes(_outside, [0]);
        Assert.Null(Prerequisite.Find(Naming("../" + Path.GetFileName(_outside)), _dir));
        Assert.Null(Prerequisite.Find(Naming(_outside), _dir));
    }

    [Theory]
    [InlineData(0, PrerequisiteOutcome.Installed)]
    [InlineData(1638, PrerequisiteOutcome.Installed)] // a newer version is already there
    [InlineData(3010, PrerequisiteOutcome.RestartNeeded)]
    [InlineData(1641, PrerequisiteOutcome.RestartNeeded)]
    [InlineData(1602, PrerequisiteOutcome.Declined)] // cancelled in its window
    [InlineData(1223, PrerequisiteOutcome.Declined)] // the UAC prompt declined
    [InlineData(unchecked((int)0x800704C7), PrerequisiteOutcome.Declined)] // the same, as WiX installers report it
    [InlineData(1603, PrerequisiteOutcome.Failed)]
    public void The_installers_exit_code_says_how_it_went(int exitCode, PrerequisiteOutcome outcome) =>
        Assert.Equal(outcome, Prerequisite.Outcome(exitCode));
}
