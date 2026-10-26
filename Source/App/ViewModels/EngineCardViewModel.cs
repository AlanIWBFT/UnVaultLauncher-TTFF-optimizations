using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UnVault.Core.Epic;
using UnVault.Core.Install;
using UnVault.Core.Util;

namespace UnVault.App.ViewModels;

/// <summary>One engine version in the library: installed, installable, or both.</summary>
public partial class EngineCardViewModel : ViewModelBase
{
    private readonly MainViewModel _owner;

    public EngineCardViewModel(MainViewModel owner, string appName, LocalEngine? local, EpicAsset? owned)
    {
        _owner = owner;
        AppName = appName;
        Local = local is { Exists: true } ? local : null;
        StaleRecord = local is { Exists: false } ? local : null;
        Owned = owned;
        Version = EngineLibrary.ParseVersion(appName);
    }

    public string AppName { get; }
    public Version Version { get; }
    public LocalEngine? Local { get; }
    public LocalEngine? StaleRecord { get; }
    public EpicAsset? Owned { get; }

    public bool IsInstalled => Local is not null;
    public bool IsStale => StaleRecord is not null && Local is null;
    public bool HasUpdate => Local is not null && Owned is not null && Local.BuildVersion != Owned.BuildVersion;
    public bool IsUpToDate => IsInstalled && !HasUpdate;

    /// <summary>"5.8", or the raw suffix for variants like "4.27Chaos".</summary>
    public string VersionLabel => AppName.StartsWith("UE_", StringComparison.Ordinal) ? AppName[3..] : AppName;

    /// <summary>The installed build, else the build an install would get, else what a stale record claims: "5.7.4".</summary>
    public string ShownVersion => EngineLibrary.ShortBuildVersion(Local?.BuildVersion ?? Owned?.BuildVersion ?? StaleRecord?.BuildVersion ?? VersionLabel);

    /// <summary>Its name elsewhere (operations, dialogs); the card itself shows only the version, on its tile.</summary>
    public string Title => "Unreal Engine " + ShownVersion;

    /// <summary>Only a status that needs a look gets a chip: an installed engine that's up to date needs none.</summary>
    public bool ShowsStatus => HasUpdate || IsStale;

    public string StatusText =>
        HasUpdate ? Localized.Format(Strings.EngineUpdateAvailable, EngineLibrary.ShortBuildVersion(Owned!.BuildVersion))
        : IsInstalled ? Strings.Installed
        : IsStale ? Strings.FilesMissing
        : Strings.NotInstalled;

    public string? Details =>
        Local is not null ? $"{Local.Directory}  ·  {ByteSize.Format(Local.InstallSize)}"
        : IsStale ? Localized.Format(Strings.EGLFolderGone, StaleRecord!.Directory)
        : null;

    public string? OriginText => Local?.Kind switch
    {
        LocalInstallKind.UnVault => Strings.OriginUnVault,
        LocalInstallKind.AdoptedFromEGL => Strings.OriginAdoptedFromEGL,
        LocalInstallKind.EGL => Strings.OriginEGL,
        _ => null,
    };

    /// <summary>Only for EGL records whose files are gone: reinstall that version.</summary>
    public bool CanInstall => IsStale;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(ModifyCommand), nameof(VerifyCommand), nameof(InstallPrerequisitesCommand), nameof(UpdateCommand))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    /// <summary>Its files are being replaced by a newer build's: started meanwhile, the editor would load some of each.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    public partial bool IsUpdating { get; set; }

    private bool CanLaunch => !IsUpdating;

    private bool CanUpdate => HasUpdate && IsIdle;

    /// <summary>To the build Epic has now; while there is one, Update takes Launch's place on the card, and Launch goes under ⋯.</summary>
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private void Update() => _owner.StartEngineUpdate(this);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task InstallAsync() => _owner.OpenInstallAsync(AppName);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ModifyAsync() => _owner.OpenModifyAsync(this);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Verify() => _owner.StartVerify(this);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void InstallPrerequisites() => _owner.StartPrerequisites(this);

    [RelayCommand]
    private Task OpenPluginsAsync() => _owner.OpenInstalledPluginsAsync(this);

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private void Launch() => _owner.Launch(this);

    [RelayCommand]
    private void OpenFolder() => _owner.OpenFolder(this);
}
