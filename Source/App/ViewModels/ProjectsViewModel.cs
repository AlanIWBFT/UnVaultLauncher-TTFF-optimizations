using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UnVault.App.Services;
using UnVault.Core.Projects;

namespace UnVault.App.ViewModels;

/// <summary>
/// The Projects tab: the projects the editor has opened, and those in the project folders (Settings), newest first. Each
/// opens in the engine it's for.
/// </summary>
public partial class ProjectsViewModel(MainViewModel owner) : ViewModelBase
{
    /// <summary>The picture's width in pixels: about twice its width on the card, so it stays sharp on a high-DPI display.</summary>
    internal const int ScreenshotPixels = 192;

    public MainViewModel Owner { get; } = owner;

    private List<ProjectCardViewModel> _all = [];
    private IReadOnlyDictionary<string, CustomEngine> _customEngines = new Dictionary<string, CustomEngine>();

    /// <summary>Pictures already read, by file, with the file's time: a reload doesn't read them again unless they changed.</summary>
    private Dictionary<string, (DateTime Written, Bitmap? Picture)> _screenshots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sizes counted before, by project file: a reload shows them while it counts again.</summary>
    private readonly Dictionary<string, long> _sizes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stops the counting of a load that's been replaced.</summary>
    private CancellationTokenSource? _counting;

    /// <summary>The projects the search leaves.</summary>
    public ObservableCollection<ProjectCardViewModel> Items { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasLoaded { get; set; }
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial string EmptyText { get; set; } = "";
    [ObservableProperty] public partial string? EmptyHint { get; set; }

    /// <summary>Where projects are found; tests give their own.</summary>
    internal Func<IReadOnlyList<UnrealProject>> FindProjects { get; set; } =
        () => ProjectLocator.FindProjects(owner.Settings.ResolveProjectFolders().Paths);

    /// <summary>The source-built engines registered on this PC; tests give their own.</summary>
    internal Func<IReadOnlyDictionary<string, CustomEngine>> FindCustomEngines { get; set; } = CustomEngines.Registered;

    /// <summary>Starts the editor; tests only look at what it would start.</summary>
    internal Action<ProcessStartInfo> StartProcess { get; set; } = start => Process.Start(start)?.Dispose();

    /// <summary>Finds the projects again (first visit, the refresh button, changed project folders).</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var (projects, customEngines) = await Task.Run(() => (FindProjects(), FindCustomEngines()));
            SetProjects(projects, customEngines);
        }
        finally
        {
            IsLoading = false;
            HasLoaded = true;
            UpdateEmpty();
        }
        _ = FillInAsync(_all); // not awaited: the refresh is done once the projects show
    }

    /// <summary>The pictures first, quick to read; then the sizes, which take longer for big projects.</summary>
    private async Task FillInAsync(IReadOnlyList<ProjectCardViewModel> cards)
    {
        await LoadScreenshotsAsync(cards);
        await CountSizesAsync(cards);
    }

    /// <summary>Shows these projects; their pictures and sizes follow (<see cref="FillInAsync"/>).</summary>
    internal void SetProjects(IReadOnlyList<UnrealProject> projects, IReadOnlyDictionary<string, CustomEngine> customEngines)
    {
        _customEngines = customEngines;
        _all = [.. projects.Select(p => new ProjectCardViewModel(this, p) { Size = _sizes.TryGetValue(p.ProjectFile, out long size) ? size : null })];
        HasLoaded = true;
        EnginesChanged();
        Filter();
    }

    /// <summary>The engines on this PC changed (a refresh, an install, a removal): which projects can open follows.</summary>
    internal void EnginesChanged()
    {
        foreach (var card in _all)
            card.EngineDirectory = EngineFor(card.Project);
    }

    /// <summary>The folder of the engine a project names: an installed version (UE_5.6), or a registered source build.</summary>
    internal string? EngineFor(UnrealProject project) =>
        project.EngineAppName is { } app
            ? Owner.LocalEngines.FirstOrDefault(e => e.Exists && string.Equals(e.AppName, app, StringComparison.OrdinalIgnoreCase))?.Directory
            : SourceBuildFor(project)?.Directory;

    internal CustomEngine? SourceBuildFor(UnrealProject project) => CustomEngines.Find(_customEngines, project.EngineAssociation);

    partial void OnSearchTextChanged(string value) => Filter();

    /// <summary>Empties the search box (the × button, or Esc).</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    private void Filter()
    {
        string search = SearchText.Trim();
        Items.Clear();
        foreach (var card in _all.Where(c => c.Matches(search)))
            Items.Add(card);
        Summary = Items.Count == _all.Count
            ? Localized.Plural(nameof(Strings.ProjectsSummary_Other), _all.Count)
            : Localized.Plural(nameof(Strings.ProjectsFiltered_Other), _all.Count, Items.Count);
        UpdateEmpty();
    }

    private void UpdateEmpty()
    {
        IsEmpty = !IsLoading && HasLoaded && Items.Count == 0;
        EmptyText = _all.Count == 0 ? Strings.NoProjects : Strings.NoProjectMatches;
        EmptyHint = _all.Count == 0 ? Strings.NoProjectsHint : null;
    }

    /// <summary>Each card's picture, one after another in the background; a missing or unreadable one keeps the initial.</summary>
    internal async Task LoadScreenshotsAsync(IReadOnlyList<ProjectCardViewModel> cards)
    {
        var known = _screenshots;
        var kept = new Dictionary<string, (DateTime Written, Bitmap? Picture)>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in cards)
        {
            string file = card.Project.ScreenshotFile;
            var (written, picture) = await Task.Run(() =>
            {
                DateTime time = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : default;
                return known.TryGetValue(file, out var had) && had.Written == time ? had : (time, time == default ? null : LoadScreenshot(file));
            });
            kept[file] = (written, picture);
            card.Screenshot = picture;
        }
        _screenshots = kept;
    }

    /// <summary>Each card's size, one project after another in the background; a newer load stops this one.</summary>
    internal async Task CountSizesAsync(IReadOnlyList<ProjectCardViewModel> cards)
    {
        _counting?.Cancel();
        var counting = _counting = new CancellationTokenSource();
        foreach (var card in cards)
        {
            try
            {
                long size = await Task.Run(() => ProjectLocator.SizeOf(card.Directory, counting.Token), counting.Token);
                _sizes[card.Project.ProjectFile] = size;
                card.Size = size;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Its folder went, or can't be read: no size, as before.
            }
        }
    }

    /// <summary>A project's file, not ours: whatever is wrong with it only costs the picture.</summary>
    internal static Bitmap? LoadScreenshot(string file)
    {
        try
        {
            if (!ThumbnailCache.IsPreviewSized(file))
                return null;
            using var stream = File.OpenRead(file);
            return Bitmap.DecodeToWidth(stream, ScreenshotPixels);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Opens the project in the editor of the engine it's for.</summary>
    internal void Open(ProjectCardViewModel card)
    {
        if (card.EngineDirectory is not { } engine)
            return;
        if (MainViewModel.EditorIn(engine) is not { } editor)
        {
            Owner.Notice = Localized.Format(Strings.NoEditorFound, engine);
            return;
        }
        var start = new ProcessStartInfo(editor) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(editor) };
        start.ArgumentList.Add(card.Project.ProjectFile);
        try
        {
            StartProcess(start);
        }
        catch (Win32Exception ex)
        {
            Owner.Notice = Localized.Format(Strings.CouldNotOpenProject, card.Name, ex.Message);
        }
    }

    internal void ShowInFolder(ProjectCardViewModel card)
    {
        if (System.IO.Directory.Exists(card.Directory))
            Owner.OpenURL(card.Directory);
    }
}
