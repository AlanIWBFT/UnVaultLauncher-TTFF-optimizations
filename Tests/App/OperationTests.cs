using System.Globalization;
using Avalonia.Headless.XUnit;
using UnVault.App.Services;
using UnVault.App.ViewModels;
using UnVault.Core.Epic;
using UnVault.Core.Install;

namespace UnVault.App.Tests;

/// <summary>
/// Operations and the Downloads tab: two never work on the same engine or Library item at once (they'd write the same
/// files), so one waits for the other; what's done, paused or failed is told in the history.
/// </summary>
public sealed class OperationTests : IDisposable
{
    /// <summary>A history of each test's own: a shared one would carry what one test leaves into the next.</summary>
    private readonly string _history = Path.Combine(Path.GetTempPath(), $"unvault-history-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_history);

    private MainViewModel WithEngines()
    {
        var viewModel = new MainViewModel(new AppServices(), _history);
        viewModel.Settings.EngineInstallRoot = @"E:\Epic Games"; // set, so nothing asks this PC's Epic Games Launcher
        viewModel.SetEngines(
            [.. new[] { "UE_5.7", "UE_5.6", "UE_5.5" }.Select(app => new LocalEngine(app, $@"E:\Epic Games\{app}", "build", 1, LocalInstallKind.UnVault))],
            [.. new[] { "UE_5.8", "UE_5.7", "UE_5.6", "UE_5.5" }.Select(app => new EpicAsset { AppName = app, BuildVersion = "build", Namespace = "ue", CatalogItemID = "test" })]);
        return viewModel;
    }

    private static OperationAction Does(OperationKind kind, string? target = null) => new(kind, target);

    /// <summary>An operation that works until it's paused. None completes here: a finished one refreshes from this PC.</summary>
    private static OperationViewModel Endless(MainViewModel owner, string subject, OperationAction action, string[] engines, string? item = null) =>
        new(owner, subject, action, async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }, engines, item);

    private sealed class Started(MainViewModel owner, string subject, OperationAction action, string[] engines, string? item = null)
    {
        public OperationViewModel Operation { get; } = Add(owner, Endless(owner, subject, action, engines, item));

        /// <summary>Its latest run, also one it started by itself after waiting. It only ends once paused, or at once when it has to wait.</summary>
        public Task Run => Operation.Run;

        private static OperationViewModel Add(MainViewModel owner, OperationViewModel operation)
        {
            owner.Operations.Insert(0, operation);
            return operation;
        }

        public Started Start()
        {
            _ = Operation.RunAsync();
            return this;
        }

        public async Task PauseAsync()
        {
            Operation.PauseCommand.Execute(null);
            await Run;
        }
    }

    private static async Task PauseAsync(OperationViewModel operation)
    {
        operation.PauseCommand.Execute(null);
        await operation.Run;
    }

    /// <summary>One that fails at once, as with a full disk.</summary>
    private static async Task<OperationViewModel> FailedAsync(MainViewModel owner, string subject, OperationAction action, string[] engines)
    {
        var failed = new OperationViewModel(owner, subject, action, (_, _, _) => throw new InstallException("The disk is full."), engines);
        owner.Operations.Insert(0, failed);
        await failed.RunAsync();
        return failed;
    }

    private static bool Busy(MainViewModel viewModel, string app) => viewModel.Engines.Single(e => e.AppName == app).IsBusy;

    [AvaloniaFact]
    public async Task A_second_operation_on_the_same_engine_waits_for_the_first()
    {
        var viewModel = WithEngines();
        var modify = new Started(viewModel, "Unreal Engine 5.7", Does(OperationKind.ModifyEngine), ["UE_5.7"]).Start();
        var plugin = new Started(viewModel, "Greybox Tools", Does(OperationKind.InstallPlugin, "5.7"), ["UE_5.7"], "fab:greybox").Start();
        Assert.True(plugin.Run.IsCompleted, "it waits instead of starting");

        Assert.Equal((OperationState.Running, OperationState.Waiting), (modify.Operation.State, plugin.Operation.State));
        Assert.Equal("Starts once Unreal Engine 5.7 (Changing components) is done.", plugin.Operation.Message);
        Assert.Equal("Installing into UE 5.7  ·  Waiting", plugin.Operation.ActionLine);
        Assert.False(plugin.Operation.RetryCommand.CanExecute(null)); // nothing to resume: it starts by itself
        Assert.True(Busy(viewModel, "UE_5.7"));

        // The other one ends (here paused): it starts.
        await modify.PauseAsync();
        Assert.Equal(OperationState.Running, plugin.Operation.State);
        Assert.True(Busy(viewModel, "UE_5.7"));
        await plugin.PauseAsync();
        Assert.False(Busy(viewModel, "UE_5.7"));
    }

    [AvaloniaFact]
    public async Task Two_operations_on_one_Library_item_never_run_at_once()
    {
        var viewModel = WithEngines();
        var download = new Started(viewModel, "Garden Pack", Does(OperationKind.Download, "5.7"), [], "fab:garden").Start();
        var add = new Started(viewModel, "Garden Pack", Does(OperationKind.AddToProject, "Hillside"), [], "fab:garden").Start();
        Assert.True(add.Run.IsCompleted, "it waits instead of starting");

        Assert.Equal((OperationState.Running, OperationState.Waiting), (download.Operation.State, add.Operation.State));
        await download.PauseAsync();
        Assert.Equal(OperationState.Running, add.Operation.State);
        await add.PauseAsync();
    }

    /// <summary>
    /// A plugin installed into several engines at once: an operation each, on top of the list in the order picked, run one
    /// after another. Each can be paused or put in the history on its own, waiting or not.
    /// </summary>
    [AvaloniaFact]
    public async Task Operations_started_together_on_one_item_take_turns()
    {
        var viewModel = WithEngines();
        var earlier = new Started(viewModel, "Unreal Engine 5.8", Does(OperationKind.InstallEngine), ["UE_5.8"]).Start();
        OperationViewModel Install(string version) =>
            Endless(viewModel, "Greybox Tools", Does(OperationKind.InstallPlugin, version), [$"UE_{version}"], "fab:greybox");
        OperationViewModel[] installs = [Install("5.7"), Install("5.6"), Install("5.5")];
        viewModel.StartOperations(installs);
        var (first, second, third) = (installs[0], installs[1], installs[2]);

        Assert.Equal([first, second, third, earlier.Operation], viewModel.Operations);
        Assert.Equal([OperationState.Running, OperationState.Waiting, OperationState.Waiting], installs.Select(o => o.State));
        Assert.Equal("Starts once Greybox Tools (Installing into UE 5.7) is done.", third.Message);
        Assert.Equal(OperationState.Running, earlier.Operation.State); // works on other files

        // The first one ends (here paused): the next in line starts, and the last one says it now waits for that one.
        await PauseAsync(first);
        Assert.Equal([OperationState.Cancelled, OperationState.Running, OperationState.Waiting], installs.Select(o => o.State));
        Assert.Equal("Starts once Greybox Tools (Installing into UE 5.6) is done.", third.Message);

        // Paused while waiting, it keeps out of the queue: nothing starts when the second one ends.
        Assert.True(third.PauseCommand.CanExecute(null));
        third.PauseCommand.Execute(null);
        Assert.Equal((OperationState.Cancelled, "Resume"), (third.State, third.RetryText));
        await PauseAsync(second);
        Assert.All(installs, o => Assert.Equal(OperationState.Cancelled, o.State));

        // Resumed while another runs on the item, it waits its turn again; put in the history meanwhile, it's paused there.
        _ = third.RunAsync();
        _ = first.RunAsync();
        Assert.Equal((OperationState.Running, OperationState.Waiting), (third.State, first.State));
        Assert.True(first.MoveToHistoryCommand.CanExecute(null));
        first.MoveToHistoryCommand.Execute(null);
        Assert.Equal(("Greybox Tools", "Install into UE 5.7 paused"), (viewModel.History[0].Name, viewModel.History[0].Line));
        Assert.DoesNotContain(first, viewModel.Operations);

        await PauseAsync(third);
        await earlier.PauseAsync();
        Assert.False(viewModel.HasRunningOperations);
    }

    /// <summary>An update of a plugin in several engines holds all of them, and one operation ending frees only its own.</summary>
    [AvaloniaFact]
    public async Task An_engine_stays_busy_until_the_last_operation_on_it_ends()
    {
        var viewModel = WithEngines();
        var update = new Started(viewModel, "Greybox Tools", Does(OperationKind.Update), ["UE_5.7", "UE_5.6"], "fab:greybox").Start();
        var verify = new Started(viewModel, "Unreal Engine 5.5", Does(OperationKind.VerifyEngine), ["UE_5.5"]).Start();
        Assert.True(Busy(viewModel, "UE_5.7") && Busy(viewModel, "UE_5.6") && Busy(viewModel, "UE_5.5"));

        await verify.PauseAsync();
        Assert.Equal([true, true, false], new[] { "UE_5.7", "UE_5.6", "UE_5.5" }.Select(app => Busy(viewModel, app)));
        Assert.False(viewModel.Engines.Single(e => e.AppName == "UE_5.6").VerifyCommand.CanExecute(null));

        await update.PauseAsync();
        Assert.All(viewModel.Engines, e => Assert.False(e.IsBusy));
    }

    /// <summary>Offered again while paused, a version could be installed twice into the same folder; put in the history, it's offered again.</summary>
    [AvaloniaFact]
    public async Task A_paused_install_keeps_its_version_out_of_the_install_list()
    {
        var viewModel = WithEngines();
        Assert.Equal(["UE_5.8"], viewModel.InstallableEngines.Select(e => e.AppName));

        var install = new Started(viewModel, "Unreal Engine 5.8", Does(OperationKind.InstallEngine), ["UE_5.8"]).Start();
        Assert.Empty(viewModel.InstallableEngines);
        await install.PauseAsync();
        Assert.Empty(viewModel.InstallableEngines); // waiting for Retry

        install.Operation.MoveToHistoryCommand.Execute(null);
        Assert.Equal(["UE_5.8"], viewModel.InstallableEngines.Select(e => e.AppName));
    }

    /// <summary>Beneath what it works on: what it does, and the step it's on unless that says the same.</summary>
    [AvaloniaFact]
    public void A_running_operation_says_what_it_does_and_its_step()
    {
        var viewModel = WithEngines();
        var verify = new OperationViewModel(viewModel, "Unreal Engine 5.6", Does(OperationKind.VerifyEngine), (_, _, _) => Task.FromResult<string?>(null))
            { Phase = "Reading manifest" };
        Assert.Equal("Verifying  ·  Reading manifest", verify.ActionLine);
        verify.Phase = "Verifying";
        Assert.Equal("Verifying", verify.ActionLine);
        Assert.Equal("Installing into UE 5.6  ·  Downloading",
            new OperationViewModel(viewModel, "Greybox Tools", Does(OperationKind.InstallPlugin, "5.6"), (_, _, _) => Task.FromResult<string?>(null))
                { Phase = "Downloading" }.ActionLine);
    }

    /// <summary>Done ones go into the history at once, saying what came of them; it's still there after a restart.</summary>
    [AvaloniaFact]
    public async Task A_finished_operation_goes_into_the_history_and_stays_there()
    {
        var viewModel = WithEngines();
        bool repaired = false;
        var verify = new OperationViewModel(viewModel, "Unreal Engine 5.6", Does(OperationKind.VerifyEngine), (operation, _, _) =>
        {
            operation.OfferFollowUp("Repair", () => repaired = true);
            return Task.FromResult<string?>("3 of 120 files missing or damaged");
        }, ["UE_5.6"]);
        viewModel.Operations.Insert(0, verify);
        await verify.RunAsync();

        Assert.Empty(viewModel.Operations);
        var entry = Assert.Single(viewModel.History);
        Assert.Equal(("Unreal Engine 5.6", "Verified  ·  3 of 120 files missing or damaged", true), (entry.Name, entry.Line, entry.IsCompleted));
        // The next step it offered goes with it, once.
        Assert.True(entry.HasFollowUp);
        entry.RunFollowUpCommand.Execute(null);
        Assert.True(repaired);
        Assert.False(entry.HasFollowUp);

        var restarted = new MainViewModel(new AppServices(), _history);
        var saved = Assert.Single(restarted.History);
        Assert.Equal((entry.Name, entry.Line), (saved.Name, saved.Line));
        Assert.False(saved.HasFollowUp); // what it would run isn't kept

        viewModel.ClearHistoryCommand.Execute(null);
        Assert.Empty(new MainViewModel(new AppServices(), _history).History);
    }

    /// <summary>Paused and failed ones stay in progress until resumed, or put in the history by hand.</summary>
    [AvaloniaFact]
    public async Task Paused_and_failed_operations_wait_until_moved_to_the_history()
    {
        var viewModel = WithEngines();
        var paused = new Started(viewModel, "Garden Pack", Does(OperationKind.Download, "5.7"), [], "fab:garden").Start();
        await paused.PauseAsync();
        var failed = await FailedAsync(viewModel, "Greybox Tools", Does(OperationKind.InstallPlugin, "5.7"), ["UE_5.7"]);

        Assert.Equal(["Retry", "Resume"], viewModel.Operations.Select(o => o.RetryText));
        Assert.Empty(viewModel.History);

        paused.Operation.MoveToHistoryCommand.Execute(null);
        failed.MoveToHistoryCommand.Execute(null);

        Assert.Empty(viewModel.Operations);
        Assert.Equal(
            [("Greybox Tools", "Install into UE 5.7 failed: The disk is full.", true),
             ("Garden Pack", "Download for UE 5.7 paused", false)],
            viewModel.History.Select(h => (h.Name, h.Line, h.IsFailed)));
    }

    /// <summary>Nothing resumes after a restart, so closing the app puts what's unfinished into the history, newest on top.</summary>
    [AvaloniaFact]
    public async Task Closing_the_app_puts_unfinished_operations_into_the_history()
    {
        var viewModel = WithEngines();
        var paused = new Started(viewModel, "Garden Pack", Does(OperationKind.Download, "5.7"), [], "fab:garden").Start();
        await paused.PauseAsync();
        await FailedAsync(viewModel, "Greybox Tools", Does(OperationKind.InstallPlugin, "5.7"), ["UE_5.7"]);
        var running = new Started(viewModel, "Unreal Engine 5.8", Does(OperationKind.InstallEngine), ["UE_5.8"]).Start();
        try
        {
            viewModel.RecordUnfinished();

            Assert.Empty(viewModel.Operations);
            Assert.Equal(
                [("Unreal Engine 5.8", "Install paused"),
                 ("Greybox Tools", "Install into UE 5.7 failed: The disk is full."),
                 ("Garden Pack", "Download for UE 5.7 paused")],
                viewModel.History.Select(h => (h.Name, h.Line)));
            Assert.Equal(3, new MainViewModel(new AppServices(), _history).History.Count); // saved
        }
        finally
        {
            await running.PauseAsync();
        }
    }

    /// <summary>The tabs' lines: the Downloads tab's moves while anything runs; a failed operation or an engine update wants a look.</summary>
    [AvaloniaFact]
    public async Task Tabs_are_underlined_while_work_runs_or_something_wants_a_look()
    {
        var viewModel = WithEngines();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.False(viewModel.HasRunningOperations);

        var download = new Started(viewModel, "Garden Pack", Does(OperationKind.Download, "5.7"), [], "fab:garden").Start();
        var verify = new Started(viewModel, "Unreal Engine 5.6", Does(OperationKind.VerifyEngine), ["UE_5.6"]).Start();
        Assert.True(viewModel.HasRunningOperations);
        await download.PauseAsync();
        Assert.True(viewModel.HasRunningOperations);
        raised.Clear();
        await verify.PauseAsync();
        Assert.False(viewModel.HasRunningOperations); // paused: nothing is going on
        Assert.Contains(nameof(MainViewModel.HasRunningOperations), raised);

        // Failed: until it's retried or put in the history.
        Assert.False(viewModel.HasFailedOperations);
        raised.Clear();
        var failed = await FailedAsync(viewModel, "Greybox Tools", Does(OperationKind.InstallPlugin, "5.7"), ["UE_5.7"]);
        Assert.Equal((true, "1 operation failed"), (viewModel.HasFailedOperations, viewModel.FailedOperationsText));
        Assert.Contains(nameof(MainViewModel.HasFailedOperations), raised);
        failed.MoveToHistoryCommand.Execute(null);
        Assert.Equal((false, null), (viewModel.HasFailedOperations, viewModel.FailedOperationsText));

        // An engine Epic has a newer build of.
        Assert.False(viewModel.HasEngineUpdates);
        viewModel.SetEngines([new LocalEngine("UE_5.7", @"E:\Epic Games\UE_5.7", "build-1", 1, LocalInstallKind.UnVault)],
            [new EpicAsset { AppName = "UE_5.7", BuildVersion = "build-2", Namespace = "ue", CatalogItemID = "test" }]);
        Assert.Equal((true, "1 engine has an update"), (viewModel.HasEngineUpdates, viewModel.EngineUpdatesText));
    }

    /// <summary>Prerequisites run on their own (from an engine's menu) say so in the history.</summary>
    [Fact]
    public void A_prerequisites_install_says_how_it_went()
    {
        HistoryEntryViewModel Entry(OperationOutcome outcome, string? message) =>
            new(new OperationRecord("Unreal Engine 5.8.3", outcome, message, DateTimeOffset.Now) { Kind = OperationKind.InstallPrerequisites });
        Assert.Equal("Prerequisites installed", Entry(OperationOutcome.Completed, null).Line);
        Assert.Equal("Prerequisites installed  ·  restart Windows to finish installing prerequisites",
            Entry(OperationOutcome.Completed, "restart Windows to finish installing prerequisites").Line);
        Assert.Equal("Prerequisites install failed: Windows asked for permission to install them, and it was declined.",
            Entry(OperationOutcome.Failed, "Windows asked for permission to install them, and it was declined.").Line);
    }

    /// <summary>Entries saved before operations kept their parts hold a sentence each: they show as they were.</summary>
    [Fact]
    public void Older_history_entries_show_as_they_were_saved()
    {
        var older = new HistoryEntryViewModel(new OperationRecord("Removing Edge Smoother from UE 5.5", OperationOutcome.Completed,
            "Removed from UE 5.5; freed 12 MB.", DateTimeOffset.Now));
        Assert.Equal(("Removing Edge Smoother from UE 5.5", "Removed from UE 5.5; freed 12 MB."), (older.Name, older.Line));
    }

    /// <summary>
    /// Headings in the app's language, whatever Windows' regional format is: here an English interface with Russian
    /// regional settings, which would otherwise give "четверг" and "20 сентября".
    /// </summary>
    [Fact]
    public void History_is_grouped_by_day()
    {
        var (culture, language) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        try
        {
            var today = new DateTime(2026, 10, 11); // a Sunday
            Assert.Equal(
                ["Today", "Yesterday", "Thursday", "September 20", "Wednesday, December 31, 2025"],
                new[] { today, today.AddDays(-1), new DateTime(2026, 10, 8), new DateTime(2026, 9, 20), new DateTime(2025, 12, 31) }
                    .Select(day => HistoryDayViewModel.HeadingFor(day, today)));

            HistoryEntryViewModel At(DateTime when) =>
                new(new OperationRecord("Garden Pack", OperationOutcome.Completed, null, new DateTimeOffset(when, TimeZoneInfo.Local.GetUtcOffset(when))));
            var days = HistoryDayViewModel.Group([At(today.AddHours(15)), At(today.AddHours(9)), At(today.AddDays(-1).AddHours(20))], today);
            Assert.Equal([("Today", 2), ("Yesterday", 1)], days.Select(d => (d.Heading, d.Entries.Count)));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, language);
        }
    }

    [Fact]
    public void The_saved_history_keeps_the_newest()
    {
        string path = Path.Combine(Path.GetTempPath(), $"unvault-history-{Guid.NewGuid():N}.json");
        try
        {
            var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
            OperationHistory.Save(Enumerable.Range(0, OperationHistory.MaxEntries + 20)
                .Select(i => new OperationRecord($"Download {i}", OperationOutcome.Completed, null, start.AddMinutes(-i))), path);

            var loaded = OperationHistory.Load(path);
            Assert.Equal(OperationHistory.MaxEntries, loaded.Count);
            Assert.Equal("Download 0", loaded[0].Title);

            File.WriteAllText(path, "{ damaged");
            Assert.Empty(OperationHistory.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
