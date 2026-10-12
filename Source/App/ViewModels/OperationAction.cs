using UnVault.App.Services;

namespace UnVault.App.ViewModels;

/// <summary>
/// What an operation does to its subject (an engine, a Library item), kept in parts so the Downloads tab can say it as it
/// runs ("Installing into UE 5.6"), once done ("Installed into UE 5.6"), and when it stopped ("Install into UE 5.6 paused").
/// </summary>
/// <param name="Target">Where to, for the kinds that have one: an engine version ("5.6") or a project's name.</param>
public sealed record OperationAction(OperationKind Kind, string? Target = null)
{
    public string Running => Say(Kind switch
    {
        OperationKind.InstallEngine => Strings.ActionInstallEngineRunning,
        OperationKind.ModifyEngine => Strings.ActionModifyEngineRunning,
        OperationKind.VerifyEngine => Strings.ActionVerifyEngineRunning,
        OperationKind.RepairEngine => Strings.ActionRepairEngineRunning,
        OperationKind.InstallPlugin => Strings.ActionInstallPluginRunning,
        OperationKind.AddToProject => Strings.ActionAddToProjectRunning,
        OperationKind.CreateProject => Strings.ActionCreateProjectRunning,
        OperationKind.Download => Strings.ActionDownloadRunning,
        OperationKind.RemovePlugin => Strings.ActionRemovePluginRunning,
        OperationKind.InstallPrerequisites => Strings.ActionInstallPrerequisitesRunning,
        OperationKind.InstallToProject => Strings.ActionInstallToProjectRunning,
        _ => Strings.ActionUpdateRunning,
    });

    public string Done => Say(Kind switch
    {
        OperationKind.InstallEngine => Strings.ActionInstallEngineDone,
        OperationKind.ModifyEngine => Strings.ActionModifyEngineDone,
        OperationKind.VerifyEngine => Strings.ActionVerifyEngineDone,
        OperationKind.RepairEngine => Strings.ActionRepairEngineDone,
        OperationKind.InstallPlugin => Strings.ActionInstallPluginDone,
        OperationKind.AddToProject => Strings.ActionAddToProjectDone,
        OperationKind.CreateProject => Strings.ActionCreateProjectDone,
        OperationKind.Download => Strings.ActionDownloadDone,
        OperationKind.RemovePlugin => Strings.ActionRemovePluginDone,
        OperationKind.InstallPrerequisites => Strings.ActionInstallPrerequisitesDone,
        OperationKind.InstallToProject => Strings.ActionInstallToProjectDone,
        _ => Strings.ActionUpdateDone,
    });

    /// <summary>The action named, for saying how it ended otherwise: "{name} paused", "{name} failed: …".</summary>
    public string Name => Say(Kind switch
    {
        OperationKind.InstallEngine => Strings.ActionInstallEngineName,
        OperationKind.ModifyEngine => Strings.ActionModifyEngineName,
        OperationKind.VerifyEngine => Strings.ActionVerifyEngineName,
        OperationKind.RepairEngine => Strings.ActionRepairEngineName,
        OperationKind.InstallPlugin => Strings.ActionInstallPluginName,
        OperationKind.AddToProject => Strings.ActionAddToProjectName,
        OperationKind.CreateProject => Strings.ActionCreateProjectName,
        OperationKind.Download => Strings.ActionDownloadName,
        OperationKind.RemovePlugin => Strings.ActionRemovePluginName,
        OperationKind.InstallPrerequisites => Strings.ActionInstallPrerequisitesName,
        OperationKind.InstallToProject => Strings.ActionInstallToProjectName,
        _ => Strings.ActionUpdateName,
    });

    private string Say(string text) => Target is null ? text : Localized.Format(text, Target);
}
