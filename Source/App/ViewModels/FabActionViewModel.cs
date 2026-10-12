using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UnVault.Core.Fab;
using UnVault.Core.Install;
using UnVault.Core.Projects;
using UnVault.Core.Util;

namespace UnVault.App.ViewModels;

public enum FabActionMode { InstallPlugin, AddToProject, CreateProject, Download, RemovePlugin, InstallToProject }

/// <summary>Something a Fab item can go into: an engine (plugins) or a project (asset packs).</summary>
/// <param name="badge">Why it can't be picked, if anything rules it out for good.</param>
/// <param name="isUsable">Nothing rules it out; it can still be busy for a while.</param>
public sealed partial class FabTargetViewModel(string title, string subtitle, string? badge, FabVersion? version, bool isUsable, LocalEngine? engine, UnrealProject? project)
    : ViewModelBase
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public string? Badge => badge ?? (IsBusy ? Strings.BadgeEngineBusy : null);

    /// <summary>Why the badge says what it does (its tooltip).</summary>
    public string? BadgeTip { get; init; }
    public FabVersion? Version { get; } = version;
    public bool IsUsable { get; } = isUsable;

    /// <summary>Can be picked now: usable, and no operation is working on its engine.</summary>
    public bool IsAvailable => IsUsable && !IsBusy;

    public LocalEngine? Engine { get; } = engine;
    public UnrealProject? Project { get; } = project;

    /// <summary>For removal: the plugin install this row stands for.</summary>
    public FabInstall? Install { get; init; }

    /// <summary>The engine it stands for, which an operation may be working on.</summary>
    public string? EngineAppName { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge), nameof(IsAvailable), nameof(CanCheck))]
    public partial bool IsBusy { get; set; }

    /// <summary>Ticked to go into or be removed from, with any others ticked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck))]
    public partial bool IsChecked { get; set; }

    /// <summary>
    /// Can be ticked when available. One ticked stays ticked while an operation works on its engine (Confirm waits), so the
    /// plugin never goes into an engine nobody chose; it can still be unticked.
    /// </summary>
    public bool CanCheck => IsAvailable || IsChecked;
}

public sealed record FabVersionOption(FabVersion Version, string Label);

public sealed record FabEngineOption(LocalEngine Engine, string Label);

/// <summary>
/// The dialog behind a Fab row's buttons: tick the engines (plugin) or projects (asset pack, or a plugin into projects) it
/// goes into, or the engines to remove a plugin from, or pick the version and folder for a new project / a plain download.
/// </summary>
public partial class FabActionViewModel : ViewModelBase, IShowsBusy
{
    private readonly FabLibraryViewModel _library;
    private readonly FabItemViewModel _item;

    public FabActionViewModel(FabLibraryViewModel library, FabItemViewModel item, FabActionMode mode)
    {
        _library = library;
        _item = item;
        Mode = mode;
        Title = mode switch
        {
            FabActionMode.InstallPlugin => Localized.Format(Strings.ActionTitleInstall, item.Title),
            FabActionMode.AddToProject => Localized.Format(Strings.ActionTitleAddToProject, item.Title),
            FabActionMode.InstallToProject => Localized.Format(Strings.ActionTitleInstallToProject, item.Title),
            FabActionMode.CreateProject => Localized.Format(Strings.ActionTitleCreateProject, item.Title),
            FabActionMode.RemovePlugin => Localized.Format(Strings.ActionTitleRemove, item.Title),
            _ => Localized.Format(Strings.ActionTitleDownload, item.Title),
        };
        ConfirmText = mode switch
        {
            FabActionMode.InstallPlugin or FabActionMode.InstallToProject => Strings.Install,
            FabActionMode.AddToProject => Strings.ActionAddToProject,
            FabActionMode.CreateProject => Strings.ActionCreateProject,
            FabActionMode.RemovePlugin => Strings.Remove,
            _ => Strings.ActionDownload,
        };
        Subtitle = $"{item.KindLabel}  ·  {(mode == FabActionMode.RemovePlugin ? item.InstalledText : item.EngineVersionsText)}";
    }

    public FabActionMode Mode { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string ConfirmText { get; }

    public bool ShowsTargets => Mode is FabActionMode.InstallPlugin or FabActionMode.AddToProject or FabActionMode.RemovePlugin or FabActionMode.InstallToProject;

    /// <summary>Goes into projects (an asset pack, or a plugin into their Plugins folders) rather than engines.</summary>
    private bool ForProjects => Mode is FabActionMode.AddToProject or FabActionMode.InstallToProject;

    public bool ShowsVersionChoice => Mode is FabActionMode.CreateProject or FabActionMode.Download;
    public bool ShowsProjectLocation => Mode == FabActionMode.CreateProject;
    public bool CanBrowseProject => ForProjects;
    public bool ShowsProjectSearch => ForProjects;
    public string TargetsHeading => ForProjects ? Strings.SectionProjects : Strings.SectionEngines;

    public ObservableCollection<FabTargetViewModel> Targets { get; } = [];

    /// <summary>The ticked ones, in the list's order; for projects, also those the search hides.</summary>
    public IReadOnlyList<FabTargetViewModel> Chosen =>
        [.. (ForProjects ? _projects : (IEnumerable<FabTargetViewModel>)Targets).Where(t => t.IsChecked)];

    /// <summary>How many are ticked, beside the buttons: the search can hide some of them.</summary>
    [ObservableProperty] public partial string? ChosenText { get; set; }

    /// <summary>Add to Project: every project found, by name; <see cref="Targets"/> lists the ones matching the search.</summary>
    private readonly List<FabTargetViewModel> _projects = [];

    /// <summary>Engines built from source on this PC, which projects name by ID instead of version.</summary>
    private IReadOnlyDictionary<string, CustomEngine> _customEngines = new Dictionary<string, CustomEngine>();

    /// <summary>Names as people read them: case doesn't matter, and "Project2" comes before "Project10".</summary>
    private static readonly StringComparer ByName =
        StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    /// <summary>Narrows the project list by name (not folder: projects usually share one, like "Unreal Projects").</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearProjectSearchCommand))]
    public partial string ProjectSearch { get; set; } = "";

    partial void OnProjectSearchChanged(string value) => FilterProjects();
    public ObservableCollection<FabVersionOption> VersionChoices { get; } = [];

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string? EmptyText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial FabVersionOption? SelectedVersion { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string ProjectParent { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string ProjectName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string? Note { get; set; }

    public bool HasNote => Note is not null;

    partial void OnSelectedVersionChanged(FabVersionOption? value) => Note = value is null ? null : DescribeSource(value.Version, 1);

    /// <summary>Listens to a target's tick, which changes the note, the count and Confirm.</summary>
    private FabTargetViewModel Tickable(FabTargetViewModel target)
    {
        target.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FabTargetViewModel.IsChecked))
                OnChosenChanged();
        };
        return target;
    }

    private void OnChosenChanged()
    {
        var chosen = Chosen;
        if (Mode == FabActionMode.RemovePlugin)
        {
            Note = DescribeRemoval(chosen);
        }
        else
        {
            // Where each copy comes from, once per copy: projects on one engine version take the same.
            var versions = chosen.Select(t => t.Version).OfType<FabVersion>().ToList();
            int downloads = versions.Count(v => !v.IsDownloaded);
            var notes = versions.DistinctBy(v => v.ArtifactID, StringComparer.OrdinalIgnoreCase)
                .Select(v => DescribeSource(v, downloads)).OfType<string>().Distinct().ToList();
            Note = notes.Count == 0 ? null : string.Join(Environment.NewLine, notes);
        }
        ChosenText = chosen.Count == 0 ? null
            : ForProjects ? Localized.Plural(nameof(Strings.ProjectsChosen_Other), chosen.Count)
            : Localized.Plural(nameof(Strings.EnginesChosen_Other), chosen.Count);
        if (Mode == FabActionMode.InstallToProject && !_enginePicked && chosen.FirstOrDefault()?.Project?.EngineAppName is { } projectEngine
            && RecompileEngines.FirstOrDefault(o => string.Equals(o.Engine.AppName, projectEngine, StringComparison.OrdinalIgnoreCase)) is { } match)
            PickEngine(match); // the first project's engine, until the user picks one
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Install to Project: compile the plugin with an installed engine first, for one its binaries weren't built for.</summary>
    public bool ShowsRecompile => Mode == FabActionMode.InstallToProject;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial bool Recompile { get; set; }

    /// <summary>Visual Studio with C++ is on this PC, and an engine to compile with.</summary>
    [ObservableProperty] public partial bool CanRecompile { get; set; }

    /// <summary>Why recompiling can't be ticked; null when it can.</summary>
    [ObservableProperty] public partial string? RecompileNote { get; set; }

    public ObservableCollection<FabEngineOption> RecompileEngines { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial FabEngineOption? RecompileEngine { get; set; }

    /// <summary>Whether Visual Studio with C++ is there; tests answer it themselves.</summary>
    internal Func<bool> FindCompiler { get; init; } = VisualStudio.HasCppCompiler;

    /// <summary>Whether an engine has the tool to compile with; tests answer it themselves.</summary>
    internal Func<LocalEngine, bool> CanCompileWith { get; init; } = engine => PluginBuilder.CanBuildWith(engine.Directory);

    private bool _enginePicked, _pickingEngine;

    partial void OnRecompileEngineChanged(FabEngineOption? value)
    {
        if (!_pickingEngine)
            _enginePicked = true;
    }

    private void PickEngine(FabEngineOption? option)
    {
        _pickingEngine = true;
        RecompileEngine = option;
        _pickingEngine = false;
    }

    /// <summary>The engines to compile with, and whether Visual Studio is there. Separate from loading so tests can call it.</summary>
    internal async Task LoadCompilersAsync()
    {
        foreach (var engine in _library.Owner.LocalEngines.Where(e => e.Exists && CanCompileWith(e)))
            RecompileEngines.Add(new FabEngineOption(engine, $"Unreal Engine {engine.AppName[3..]}"));
        bool compiler = await Task.Run(FindCompiler);
        CanRecompile = compiler && RecompileEngines.Count > 0;
        RecompileNote = !compiler ? Strings.RecompileNeedsVisualStudio : RecompileEngines.Count == 0 ? Strings.RecompileNeedsEngine : null;
        if (RecompileEngine is null)
            PickEngine(RecompileEngines.FirstOrDefault());
    }

    private bool SignedIn => _library.Owner.IsSignedIn;

    /// <summary>A version can be used if it's already downloaded, or can be downloaded (signed in, in the online library).</summary>
    private bool CanGet(FabVersion version) => version.IsDownloaded || (SignedIn && version.Library is not null && _item.Library is not null);

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            switch (Mode)
            {
                case FabActionMode.InstallPlugin: LoadEngines(); break;
                case FabActionMode.AddToProject: await LoadProjectsAsync(); break;
                case FabActionMode.InstallToProject: await LoadCompilersAsync(); await LoadProjectsAsync(); break;
                case FabActionMode.CreateProject: LoadVersions(onlyDownloadable: false); SetProjectDefaults(); break;
                case FabActionMode.Download: LoadVersions(onlyDownloadable: true); break;
                case FabActionMode.RemovePlugin: await LoadInstallsAsync(); break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EmptyText = Localized.Format(Strings.TargetsLookupFailed, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadEngines()
    {
        foreach (var engine in _library.Owner.LocalEngines.Where(e => e.Exists))
        {
            var version = BestVersion(_item.Versions.Where(v => SameEngine(v.EngineAppName, engine.AppName)));
            bool installed = _item.Installs.Any(i => SamePath(i.EngineDirectory, engine.Directory));
            string? badge = installed ? Strings.Installed
                : version is null ? Strings.BadgeNoVersionForEngine
                : !CanGet(version) ? Strings.BadgeSignInToDownload
                : null;
            Targets.Add(Tickable(new FabTargetViewModel($"Unreal Engine {engine.AppName[3..]}", engine.Directory, badge, version,
                isUsable: badge is null, engine, project: null) { EngineAppName = engine.AppName }));
        }
        UpdateBusy();
        EmptyText = Targets.Count == 0 ? Strings.NoInstalledEngines : !Targets.Any(t => t.IsUsable) ? Strings.PluginFitsNoEngine : null;
    }

    /// <summary>
    /// The first engine it can go into or be removed from is ticked to begin with, once: unticked by the user, it stays so.
    /// </summary>
    private bool _ticked;

    /// <summary>
    /// Marks the engines an operation is working on, as they start and end while the dialog is open. A busy engine can't
    /// be ticked; one already ticked stays so (Confirm waits).
    /// </summary>
    public void UpdateBusy()
    {
        foreach (var target in Targets)
        {
            if (target.EngineAppName is { } app)
                target.IsBusy = _library.Owner.IsEngineBusy(app);
        }
        if (!ForProjects && !_ticked && Targets.FirstOrDefault(t => t.IsAvailable) is { } first)
        {
            first.IsChecked = true;
            _ticked = true;
        }
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadInstallsAsync()
    {
        var installs = _item.Installs.OrderByDescending(i => EngineLibrary.ParseVersion(i.EngineAppName)).ToList();
        var sizes = await Task.Run(() => installs.Select(i => EnginePlugins.FolderSize(i.Folder)).ToList());
        for (int i = 0; i < installs.Count; i++)
        {
            var install = installs[i];
            // Removing works the same whoever installed it, so the note below says who did; a badge only says why a row is off.
            string? badge = install.CanRemove ? null : Strings.BadgeRemoveWithEGL;
            string where = !install.CanRemove ? Strings.InstalledOutsideMarketplace
                : sizes[i] > 0 ? $"{install.Folder}  ·  {ByteSize.Format(sizes[i])}"
                : install.Folder;
            Targets.Add(Tickable(new FabTargetViewModel($"Unreal Engine {install.EngineAppName[3..]}", where,
                badge, version: null, isUsable: install.CanRemove, engine: null, project: null)
            {
                Install = install,
                EngineAppName = install.EngineAppName,
                BadgeTip = install.CanRemove ? null : Strings.RemovalAmongEngineFolders,
            }));
        }
        UpdateBusy();
        OnChosenChanged(); // nothing removable: the note says why
        EmptyText = Targets.Count == 0 ? Strings.PluginInNoEngine : null;
    }

    private async Task LoadProjectsAsync()
    {
        var (projects, customEngines) = await Task.Run(() =>
            (ProjectLocator.FindProjects(_library.Owner.Settings.ResolveProjectFolders().Paths), CustomEngines.Registered()));
        ShowProjects(projects, customEngines);
    }

    /// <summary>Lists the projects by name. Separate from loading so tests can supply projects and engines.</summary>
    internal void ShowProjects(IEnumerable<UnrealProject> projects, IReadOnlyDictionary<string, CustomEngine>? customEngines = null)
    {
        _customEngines = customEngines ?? new Dictionary<string, CustomEngine>();
        _projects.Clear();
        _projects.AddRange(projects.OrderBy(p => p.Name, ByName).Select(ProjectTarget));
        FilterProjects(); // nothing ticked: the projects are the user's pick
    }

    /// <summary>Ticks stay on the projects the search hides, and count.</summary>
    private void FilterProjects()
    {
        string search = ProjectSearch.Trim();
        Targets.Clear();
        foreach (var target in _projects.Where(t => t.Title.Contains(search, StringComparison.OrdinalIgnoreCase)))
            Targets.Add(target);
        EmptyText = _projects.Count == 0 ? Localized.Format(Strings.NoProjectsFound, Strings.ChooseProjectFile)
            : Targets.Count == 0 ? Strings.NoProjectsMatch
            : null;
    }

    [RelayCommand(CanExecute = nameof(CanClearProjectSearch))]
    private void ClearProjectSearch() => ProjectSearch = "";

    private bool CanClearProjectSearch() => ProjectSearch.Length > 0;

    private FabTargetViewModel ProjectTarget(UnrealProject project)
    {
        // Launcher engines go by version ("5.7"); custom ones by an ID, looked up among the engines built on this PC.
        string? engine = project.EngineAppName;
        string engineName = $"UE {project.EngineAssociation}";
        if (engine is null)
        {
            var custom = CustomEngines.Find(_customEngines, project.EngineAssociation);
            engine = custom?.Version is { } customVersion ? "UE_" + customVersion : null;
            engineName = engine is null ? Strings.CustomEngine : Localized.Format(Strings.CustomEngineVersion, Short(engine));
            if (engine is null)
            {
                // No telling which version it could open, and assets saved by a newer engine don't open in an older one.
                string why = custom is null
                    ? Strings.CustomEngineNotRegistered
                    : Localized.Format(Strings.CustomEngineVersionUnreadable, custom.Directory);
                return Tickable(new FabTargetViewModel(project.Name, $"{engineName}  ·  {project.Directory}",
                    custom is null ? Strings.BadgeEngineNotInstalled : Strings.BadgeEngineVersionUnknown, version: null, isUsable: false, engine: null, project)
                {
                    BadgeTip = why,
                });
            }
        }

        // The version made for this engine, else the newest one made for an older engine: newer engines open older assets.
        var engineVersion = EngineLibrary.ParseVersion(engine);
        var usable = _item.Versions.Where(CanGet).ToList();
        var version = BestVersion(usable.Where(v => SameEngine(v.EngineAppName, engine)))
            ?? usable.Where(v => EngineLibrary.ParseVersion(v.EngineAppName) <= engineVersion)
                .OrderByDescending(v => EngineLibrary.ParseVersion(v.EngineAppName)).FirstOrDefault();

        string? badge = null, tip = null;
        if (version is not null && !SameEngine(version.EngineAppName, engine))
        {
            badge = Localized.Format(Strings.BadgeLastAvailableFrom, Short(version.EngineAppName));
            tip = Localized.Format(Mode == FabActionMode.InstallToProject ? Strings.TipOlderPluginVersion : Strings.TipOlderVersionCanBeAdded,
                Short(engine), Short(version.EngineAppName));
        }
        else if (version is null && !SignedIn && _item.Versions.Any(v => EngineLibrary.ParseVersion(v.EngineAppName) <= engineVersion))
        {
            badge = Strings.BadgeSignInToDownload;
            tip = Strings.TipNotInVaultYet;
        }
        else if (version is null)
        {
            // Every version is made for a newer engine, and an engine can't open assets saved by a newer one.
            string? oldest = _item.Versions.Select(v => v.EngineAppName).Where(EngineLibrary.IsEngineApp).MinBy(EngineLibrary.ParseVersion);
            badge = Strings.BadgeUnsupportedEngine;
            tip = oldest is null ? Strings.TipMadeForNewerEngine : Localized.Format(Strings.TipMadeForNewerEngineVersion, Short(oldest));
        }

        return Tickable(new FabTargetViewModel(project.Name, $"{engineName}  ·  {project.Directory}", badge, version,
            isUsable: version is not null, engine: null, project)
        {
            BadgeTip = tip,
        });
    }

    private void LoadVersions(bool onlyDownloadable)
    {
        foreach (var version in _item.Versions
                     .Where(v => onlyDownloadable ? v.Library is not null && SignedIn : CanGet(v))
                     .GroupBy(v => v.ArtifactID).Select(g => BestVersion(g)!)
                     .OrderByDescending(v => EngineLibrary.ParseVersion(v.EngineAppName)))
        {
            string engine = $"Unreal Engine {Short(version.EngineAppName)}";
            VersionChoices.Add(new FabVersionOption(version, version.IsOutdated ? Localized.Format(Strings.VersionUpdateAvailable, engine)
                : version.IsDownloaded ? Localized.Format(Strings.VersionDownloaded, engine)
                : engine));
        }
        SelectedVersion = Mode == FabActionMode.Download
            ? VersionChoices.FirstOrDefault(o => !o.Version.IsDownloaded || o.Version.IsOutdated) ?? VersionChoices.FirstOrDefault()
            : VersionChoices.FirstOrDefault();
        EmptyText = VersionChoices.Count == 0 ? (SignedIn ? Strings.NoDownloadableVersions : Strings.SignInToDownloadItem) : null;
    }

    private void SetProjectDefaults()
    {
        ProjectParent = _library.Owner.Settings.ResolveProjectFolders().ForNewProjects;
        ProjectName = SafeFolderName(_item.Title);
    }

    [RelayCommand]
    private async Task BrowseProjectFileAsync()
    {
        if (_library.Owner.Interaction is null)
            return;
        string? file = await _library.Owner.Interaction.PickFileAsync(Strings.PickProject, null, Strings.UnrealProjectFileType, "*.uproject");
        if (file is null || ProjectLocator.Read(file) is not { } project)
            return;
        // The project just picked goes first, whatever its name, and shows even if the search wouldn't find it. One already
        // listed moves there, rather than being listed (and maybe ticked) twice.
        var target = _projects.FirstOrDefault(t => t.Project is { } listed && SamePath(listed.ProjectFile, project.ProjectFile)) ?? ProjectTarget(project);
        _projects.Remove(target);
        _projects.Insert(0, target);
        if (ProjectSearch.Length > 0)
            ProjectSearch = "";
        else
            FilterProjects();
        target.IsChecked = target.IsAvailable;
    }

    [RelayCommand]
    private async Task BrowseProjectParentAsync()
    {
        if (_library.Owner.Interaction is not null
            && await _library.Owner.Interaction.PickFolderAsync(Strings.PickProjectLocation, ProjectParent) is { } folder)
            ProjectParent = folder;
    }

    [RelayCommand]
    private void Cancel() => _library.Owner.CloseDialog();

    private bool CanConfirm() => Mode switch
    {
        FabActionMode.InstallPlugin or FabActionMode.AddToProject or FabActionMode.RemovePlugin => Chosen is { Count: > 0 } chosen && chosen.All(t => t.IsAvailable),
        FabActionMode.InstallToProject => Chosen is { Count: > 0 } chosen && chosen.All(t => t.IsAvailable) && (!Recompile || RecompileEngine is not null),
        FabActionMode.CreateProject => SelectedVersion is not null && !string.IsNullOrWhiteSpace(ProjectName) && Path.IsPathFullyQualified(ProjectParent),
        _ => SelectedVersion is not null,
    };

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        _library.Owner.CloseDialog();
        switch (Mode)
        {
            case FabActionMode.InstallPlugin:
                _library.StartPluginInstall(_item, Chosen.Select(t => (t.Version!, t.Engine!)));
                break;
            case FabActionMode.AddToProject:
                _library.StartAddToProject(_item, Chosen.Select(t => (t.Version!, t.Project!)));
                break;
            case FabActionMode.InstallToProject:
                _library.StartInstallToProject(_item, Chosen.Select(t => (t.Version!, t.Project!)), Recompile ? RecompileEngine?.Engine : null);
                break;
            case FabActionMode.CreateProject:
                _library.StartCreateProject(_item, SelectedVersion!.Version, Path.Combine(ProjectParent, ProjectName.Trim()));
                break;
            case FabActionMode.RemovePlugin:
                _library.StartRemovals(_item.Title, _item.Key, [.. Chosen.Select(t => t.Install!)]);
                break;
            default:
                _library.StartDownload(_item, SelectedVersion!.Version);
                break;
        }
    }

    /// <summary>Null when there's nothing to add: a plugin that isn't downloaded yet just downloads into the engine.</summary>
    /// <param name="targets">How many projects get a downloaded copy.</param>
    private string? DescribeSource(FabVersion version, int targets) =>
        version.IsOutdated
            ? Mode == FabActionMode.InstallPlugin
                ? Strings.NoteOlderCopyInstall
                : Strings.NoteOlderCopyUpdate
        : version.IsDownloaded ? Localized.Format(Strings.NoteAlreadyInVault, Short(version.EngineAppName))
        : Mode switch
        {
            FabActionMode.InstallPlugin => null,
            FabActionMode.AddToProject or FabActionMode.InstallToProject => Localized.Plural(nameof(Strings.NoteDownloadsAndAdds_Other), targets),
            FabActionMode.CreateProject => Strings.NoteDownloadsAndCreates,
            _ => Strings.NoteDownloadsReady,
        };

    /// <summary>
    /// Who installed it, once for the engines that share an answer; each line says which engines when they don't. With
    /// nothing it can be removed from, why not.
    /// </summary>
    private string? DescribeRemoval(IReadOnlyList<FabTargetViewModel> chosen)
    {
        if (chosen.Count == 0)
            return Targets.Count > 0 && !Targets.Any(t => t.IsUsable) ? Strings.RemovalAmongEngineFolders : null;
        var bySource = chosen.GroupBy(t => t.Install!.Source).ToList();
        var lines = bySource.Select(g => bySource.Count == 1 ? InstalledBy(g.Key)
            : Localized.Format(Strings.RemovalFrom, string.Join(", ", g.Select(t => Short(t.Install!.EngineAppName))), InstalledBy(g.Key)));
        return string.Join(Environment.NewLine, [.. lines, Strings.RemovalBreaksProjects]);
    }

    private static string InstalledBy(PluginSource source) => source switch
    {
        PluginSource.UnVault => Strings.RemovalByUnVault,
        PluginSource.EGL => Strings.RemovalByEGL,
        _ => Strings.RemovalByOther,
    };

    /// <summary>Prefer a downloaded copy (no network) over one that has to be fetched.</summary>
    private static FabVersion? BestVersion(IEnumerable<FabVersion> versions) =>
        versions.OrderByDescending(v => v.IsDownloaded).ThenByDescending(v => v.Library is not null).FirstOrDefault();

    private static bool SameEngine(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool SamePath(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
    private static string Short(string engineAppName) => engineAppName.StartsWith("UE_", StringComparison.Ordinal) ? engineAppName[3..] : engineAppName;

    /// <summary>"Modular Warehouse  V. 2" → "ModularWarehouseV2": UE project names can't have spaces.</summary>
    private static string SafeFolderName(string title)
    {
        var name = new StringBuilder();
        foreach (char c in title)
        {
            if (char.IsAsciiLetterOrDigit(c))
                name.Append(c);
        }
        return name.Length == 0 ? "NewProject" : char.IsDigit(name[0]) ? "P" + name : name.ToString();
    }
}
