using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UnVault.App.ViewModels;
using UnVault.App.Views;
using UnVault.Core.Install;
using UnVault.Core.Projects;

namespace UnVault.App.Tests;

/// <summary>The Projects tab: each project with the editor's picture of it, opened in the engine it's for.</summary>
public sealed class ProjectsTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"unvault-projects-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A project on disk; with a picture when given its colour, as the editor leaves one in Saved.</summary>
    private UnrealProject Project(string name, string association, DateTime? lastOpened = null, Color? picture = null)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_dir, "Projects", name)).FullName;
        string file = Path.Combine(folder, name + ".uproject");
        File.WriteAllText(file, $$"""{ "FileVersion": 3, "EngineAssociation": "{{association}}" }""");
        if (picture is { } color)
            Picture(Path.Combine(folder, "Saved", "AutoScreenshot.png"), color);
        return new UnrealProject(name, file, association, lastOpened);
    }

    private static void Picture(string file, Color color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var bitmap = new RenderTargetBitmap(new PixelSize(512, 512)); // square, as the editor takes them
        using (var context = bitmap.CreateDrawingContext())
        {
            var sky = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(color, 0), new GradientStop(Colors.Black, 1) },
            };
            context.FillRectangle(sky, new Rect(0, 0, 512, 512));
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(40, 46, 38)), new Rect(0, 360, 512, 152)); // ground
        }
        bitmap.Save(file, new PngBitmapEncoderOptions());
    }

    /// <summary>An engine folder with its editor, which is never started here.</summary>
    private string Engine(string name)
    {
        string engine = Path.Combine(_dir, name);
        string editor = Path.Combine(engine, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(editor)!);
        File.WriteAllText(editor, "");
        return engine;
    }

    private static Dictionary<string, CustomEngine> SourceBuilds(params CustomEngine[] engines) =>
        engines.ToDictionary(e => e.ID.Trim('{', '}'), StringComparer.OrdinalIgnoreCase);

    [AvaloniaFact]
    public void A_project_opens_in_the_engine_it_is_for_and_the_card_says_when_that_is_missing()
    {
        var viewModel = ScreenshotTests.SampleMainViewModel();
        string ue56 = Engine("UE_5.6");
        viewModel.SetEngines([new LocalEngine("UE_5.6", ue56, "5.6.1-44394996+++UE5+Release-5.6-Windows", 1, LocalInstallKind.UnVault)], []);
        var started = new List<ProcessStartInfo>();
        var projects = viewModel.Projects;
        projects.StartProcess = started.Add;
        string sourceBuild = Engine("Source");
        projects.SetProjects(
            [
                Project("Hillside", "5.6", DateTime.UtcNow),
                Project("Ridgeback", "5.3"),
                Project("Quarry", "{6F1C2A3B-0D4E-4F5A-8B6C-7D8E9F0A1B2C}"),
                Project("Loose", ""),
            ],
            SourceBuilds(new CustomEngine("{6F1C2A3B-0D4E-4F5A-8B6C-7D8E9F0A1B2C}", sourceBuild, "5.4")));
        ProjectCardViewModel Card(string name) => projects.Items.Single(c => c.Name == name);

        // What each is for, and whether it can open: only an engine that's missing gets a chip.
        Assert.Equal(["UE 5.6  ·  Last opened: Today", "UE 5.3", "Source build 5.4", "No engine set"], projects.Items.Select(c => c.DetailsText));
        Assert.Equal([true, false, true, false], projects.Items.Select(c => c.CanOpen));
        Assert.Equal([false, true, false, false], projects.Items.Select(c => c.IsEngineMissing));
        Assert.Equal("UE 5.3 isn't installed on this PC.", Card("Ridgeback").OpenTip);
        Assert.False(Card("Ridgeback").OpenCommand.CanExecute(null));

        // Open starts that engine's editor with the project, its path passed as one argument.
        Card("Hillside").OpenCommand.Execute(null);
        Card("Quarry").OpenCommand.Execute(null);
        Assert.Equal(
            [
                (Path.Combine(ue56, @"Engine\Binaries\Win64\UnrealEditor.exe"), Card("Hillside").Project.ProjectFile),
                (Path.Combine(sourceBuild, @"Engine\Binaries\Win64\UnrealEditor.exe"), Card("Quarry").Project.ProjectFile),
            ],
            started.Select(s => (s.FileName, s.ArgumentList.Single())));

        // UE 5.3 installed since: Ridgeback can open now.
        viewModel.SetEngines([.. viewModel.LocalEngines, new LocalEngine("UE_5.3", Engine("UE_5.3"), "5.3.2-29314046+++UE5+Release-5.3-Windows", 1, LocalInstallKind.UnVault)], []);
        Assert.True(Card("Ridgeback").CanOpen);
    }

    [AvaloniaFact]
    public void The_search_finds_projects_by_name_engine_or_folder()
    {
        var projects = ScreenshotTests.SampleMainViewModel().Projects;
        projects.SetProjects([Project("Hillside", "5.6"), Project("Ridgeback", "5.7"), Project("OldTown", "4.27")], SourceBuilds());
        Assert.Equal("3 Projects", projects.Summary);

        projects.SearchText = "ridge";
        Assert.Equal(["Ridgeback"], projects.Items.Select(c => c.Name));
        Assert.Equal("1 of 3 Projects", projects.Summary);

        projects.SearchText = "UE 4";
        Assert.Equal(["OldTown"], projects.Items.Select(c => c.Name));

        projects.SearchText = "nothing like it";
        Assert.Equal((true, "No project matches your search."), (projects.IsEmpty, projects.EmptyText));

        projects.ClearSearchCommand.Execute(null);
        Assert.Equal(3, projects.Items.Count);
    }

    [AvaloniaFact]
    public async Task Pictures_come_from_the_projects_Saved_folder_and_a_missing_one_leaves_the_initial()
    {
        var projects = ScreenshotTests.SampleMainViewModel().Projects;
        projects.SetProjects([Project("Hillside", "5.6", picture: Colors.SteelBlue), Project("Ridgeback", "5.7")], SourceBuilds());

        await projects.LoadScreenshotsAsync(projects.Items);

        Assert.Equal(ProjectsViewModel.ScreenshotPixels, projects.Items[0].Screenshot?.PixelSize.Width);
        Assert.Null(projects.Items[1].Screenshot);
        Assert.Equal("R", projects.Items[1].Initial);
    }

    [AvaloniaFact]
    public async Task A_projects_size_is_counted_in_the_background_and_shown_after_its_folder()
    {
        var projects = ScreenshotTests.SampleMainViewModel().Projects;
        var hillside = Project("Hillside", "5.6");
        string map = Path.Combine(hillside.Directory, "Content", "Map.umap");
        Directory.CreateDirectory(Path.GetDirectoryName(map)!);
        File.WriteAllBytes(map, new byte[3 * 1024 * 1024]);
        projects.SetProjects([hillside], SourceBuilds());
        Assert.Null(projects.Items[0].SizeText); // until it's counted

        await projects.CountSizesAsync(projects.Items);
        Assert.Equal("3.0 MB", projects.Items[0].SizeText);

        // Found again: the size counted before shows at once, while it's counted again.
        projects.SetProjects([hillside], SourceBuilds());
        Assert.Equal("3.0 MB", projects.Items[0].SizeText);
    }

    /// <summary>Show in Folder is under ⋯, where more of what can be done with a project will go.</summary>
    [AvaloniaFact]
    public void Show_in_Folder_is_in_the_More_menu()
    {
        var card = new ProjectCardView { DataContext = new ProjectCardViewModel(ScreenshotTests.SampleMainViewModel().Projects, Project("Hillside", "5.6")) };
        var window = new Window { Content = card };
        window.Show();
        var more = card.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "⋯");
        var menu = Assert.IsType<MenuFlyout>(more.Flyout);
        Assert.Equal(["Show in Folder"], menu.Items.OfType<MenuItem>().Select(i => i.Header as string));
        window.Close();
    }

    [AvaloniaFact]
    public void Projects_take_two_columns_and_one_when_the_window_is_narrow()
    {
        var viewModel = ScreenshotTests.SampleMainViewModel();
        var projects = viewModel.Projects;
        DateTime now = DateTime.UtcNow;
        projects.FindProjects = () =>
        [
            Project("Hillside", "5.6", now, Colors.SteelBlue),
            Project("Ridgeback", "5.7", now.AddDays(-1), Colors.IndianRed),
            Project("Lakeside", "5.5", now.AddDays(-3), Colors.CadetBlue),
            Project("OldTown", "4.26", now.AddDays(-40)),
            Project("Greybox", "5.7", null, Colors.DarkKhaki),
        ];
        projects.FindCustomEngines = () => SourceBuilds();
        var window = ScreenshotTests.Show(viewModel);

        viewModel.ShowProjectsCommand.Execute(null);
        ScreenshotTests.WaitFor(() => projects.HasLoaded && projects.Items.All(c => c.Size is not null));
        Assert.Equal("5 Projects", projects.Summary);
        Assert.True(projects.Items.Single(c => c.Name == "OldTown").IsEngineMissing); // UE 4.26 isn't among the sample engines
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, Columns(window));
        ScreenshotTests.Save(window, "projects.png");

        window.Width = 784;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Columns(window));
        ScreenshotTests.Save(window, "projects-narrow.png");
    }

    /// <summary>How many cards share the first row.</summary>
    private static int Columns(Window window)
    {
        var tops = window.GetVisualDescendants().OfType<ProjectCardView>().Where(c => c.IsEffectivelyVisible)
            .Select(c => c.TranslatePoint(default, window)!.Value.Y).ToList();
        return tops.Count(top => Math.Abs(top - tops[0]) < 1);
    }
}
