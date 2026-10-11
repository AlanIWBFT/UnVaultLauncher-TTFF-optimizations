using System.ComponentModel;
using System.Diagnostics;
using UnVault.Core.Manifests;
using UnVault.Core.Util;

namespace UnVault.Core.Install;

/// <summary>What came of running a build's prerequisite installer.</summary>
public enum PrerequisiteOutcome { Installed, RestartNeeded, Declined, Failed }

/// <summary>
/// The installer a build names to run once it's installed. For engines it's the Visual C++ runtime (5.6 and later) or
/// Epic's UE prerequisites, which include it; without it the editor stops at its first start, asking for the runtime. The
/// Epic Games Launcher runs it after installing, as given in the manifest.
/// </summary>
/// <param name="Installer">Its full path, inside the install folder.</param>
/// <param name="Arguments">As the build gives them ("/quiet"), or none: the installer then shows its own window.</param>
public sealed record Prerequisite(string Installer, string Arguments)
{
    /// <summary>The build's prerequisite installer, if it names one inside the install and the install has it.</summary>
    public static Prerequisite? Find(Manifest manifest, string installDir)
    {
        if (string.IsNullOrWhiteSpace(manifest.Meta.PrereqPath))
            return null;
        string root = Path.GetFullPath(installDir);
        string installer = Path.GetFullPath(Path.Combine(root, manifest.Meta.PrereqPath));
        return ContainedPath.IsInside(root, installer) && File.Exists(installer) ? new Prerequisite(installer, manifest.Meta.PrereqArgs) : null;
    }

    /// <summary>
    /// Runs it and waits for it to end. These installers start without admin rights and ask Windows for them (a UAC prompt)
    /// only when there's something to install, so a runtime that's already there costs no prompt. Stopping the wait leaves
    /// the installer running: cut off, it could leave the runtime half installed.
    /// </summary>
    public async Task<(PrerequisiteOutcome Outcome, int ExitCode)> RunAsync(CancellationToken cancellationToken)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(Installer, Arguments)
            {
                UseShellExecute = true, // so Windows can ask for admin rights, for one that wants them up front
                WorkingDirectory = Path.GetDirectoryName(Installer),
            });
        }
        catch (Win32Exception ex) // declined, if it asked for admin rights up front; or Windows wouldn't start it
        {
            return (ex.NativeErrorCode == ErrorCancelled ? PrerequisiteOutcome.Declined : PrerequisiteOutcome.Failed, ex.NativeErrorCode);
        }
        if (process is null)
            return (PrerequisiteOutcome.Failed, -1);

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken);
            return (Outcome(process.ExitCode), process.ExitCode);
        }
    }

    private const int ErrorCancelled = 1223;

    /// <summary>The same cancel as an HRESULT, as WiX installers (Epic's and Microsoft's) end with when their prompt is declined.</summary>
    private const int CancelledResult = unchecked((int)0x800704C7);

    /// <summary>Windows Installer exit codes, which these installers follow.</summary>
    internal static PrerequisiteOutcome Outcome(int exitCode) => exitCode switch
    {
        0 => PrerequisiteOutcome.Installed,
        1638 => PrerequisiteOutcome.Installed, // a newer version is already there
        3010 or 1641 => PrerequisiteOutcome.RestartNeeded, // installed; Windows needs a restart to finish
        1602 or ErrorCancelled or CancelledResult => PrerequisiteOutcome.Declined, // the UAC prompt or the installer's window
        _ => PrerequisiteOutcome.Failed,
    };
}
