using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UnVault.App.Services;
using UnVault.Core.Epic;
using UnVault.Core.Install;
using UnVault.Core.Util;

namespace UnVault.App.ViewModels;

/// <summary>Cancelled is paused; Waiting hasn't started yet, as another operation works on the same files.</summary>
public enum OperationState { Running, Completed, Failed, Cancelled, Waiting }

/// <summary>
/// One running job (install, modify, verify, repair) shown in the downloads panel. The work reports through
/// an <see cref="InstallStatus"/> that a timer polls; retry re-runs the same work, which resumes.
/// </summary>
public partial class OperationViewModel : ViewModelBase
{
    public delegate Task<string?> Work(OperationViewModel operation, InstallStatus status, CancellationToken cancellationToken);

    private readonly MainViewModel _owner;

    /// <summary>Kept for retry; dropped once done, since it can hold an engine's whole manifest.</summary>
    private Work? _work;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _speedClock = new();
    private CancellationTokenSource? _cancellation;
    private InstallStatus _status = new();
    private long _lastBytes;
    private double _bytesPerSecond;

    /// <param name="subject">What it works on, as people know it: "Unreal Engine 5.7.4", a Library item's title.</param>
    /// <param name="action">What it does to it.</param>
    /// <param name="work">Does it; returns a short note on what came of it ("freed 114 MB"), or null.</param>
    /// <param name="engines">The engines it works on (installing one, or plugins in them), by app name.</param>
    /// <param name="fabItemKey">The Library item it works on.</param>
    public OperationViewModel(MainViewModel owner, string subject, OperationAction action, Work work, IReadOnlyList<string>? engines = null,
        string? fabItemKey = null)
    {
        _owner = owner;
        _work = work;
        Subject = subject;
        Action = action;
        EngineAppNames = engines ?? [];
        FabItemKey = fabItemKey;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => UpdateProgress());
    }

    public string Subject { get; }
    public OperationAction Action { get; }

    /// <summary>Under the subject: what it does, and the step it's on unless that says the same ("Verifying").</summary>
    public string ActionLine =>
        string.Equals(Phase, Action.Running, StringComparison.OrdinalIgnoreCase) ? Action.Running : $"{Action.Running}  ·  {Phase}";

    /// <summary>The engines this works on (by app name, since engine cards are rebuilt on refresh).</summary>
    public IReadOnlyList<string> EngineAppNames { get; }

    /// <summary>The Fab library row this works on (by its key, since rows are rebuilt on refresh).</summary>
    public string? FabItemKey { get; }

    public bool WorksOn(string engineAppName) => EngineAppNames.Contains(engineAppName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Works on an engine or a Library item the other one does too: the two would write the same files.</summary>
    public bool Overlaps(OperationViewModel other) =>
        EngineAppNames.Any(other.WorksOn) || FabItemKey is not null && FabItemKey == other.FabItemKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLine))]
    public partial string Phase { get; set; } = "Starting…";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial bool IsIndeterminate { get; set; } = true;
    [ObservableProperty] public partial string ProgressText { get; set; } = "";
    [ObservableProperty] public partial string SpeedText { get; set; } = "";
    [ObservableProperty] public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsWaiting), nameof(CanPause), nameof(CanRetry), nameof(CanMoveToHistory),
        nameof(IsFailed), nameof(IsPausedOrWaiting), nameof(RetryText))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand), nameof(RetryCommand), nameof(MoveToHistoryCommand))]
    public partial OperationState State { get; set; }

    /// <summary>Its place among the waiting ones: they start in the order they began to wait.</summary>
    internal long Turn { get; private set; }

    private static long s_turns;

    /// <summary>The latest run. It ends once the operation is done, paused or failed, or at once when it has to wait.</summary>
    internal Task Run { get; private set; } = Task.CompletedTask;

    /// <summary>A next step offered when done, e.g. Repair after a verify; it goes with the operation into the history.</summary>
    public (string Text, Action Action)? FollowUp => Volatile.Read(ref _followUp) is { } offered ? (offered.Text, offered.Action) : null;

    private sealed record Offer(string Text, Action Action);
    private Offer? _followUp;

    public bool IsRunning => State == OperationState.Running;
    public bool IsWaiting => State == OperationState.Waiting;
    public bool IsFailed => State == OperationState.Failed;
    public bool IsPausedOrWaiting => State is OperationState.Cancelled or OperationState.Waiting;
    public bool CanPause => State is OperationState.Running or OperationState.Waiting;
    public bool CanRetry => State is OperationState.Failed or OperationState.Cancelled;
    public bool CanMoveToHistory => CanRetry || IsWaiting;

    /// <summary>Paused ones continue where they stopped; failed ones try again (which also continues, where it can).</summary>
    public string RetryText => IsFailed ? Strings.Retry : Strings.Resume;

    public void SetPhase(string phase) => Dispatcher.UIThread.Post(() => Phase = phase);

    /// <summary>
    /// Starts a new step with its own progress (e.g. copying after a download), so the bar restarts instead of
    /// running past 100%. Use the returned status for that step.
    /// </summary>
    public InstallStatus BeginPhase(string phase)
    {
        var status = new InstallStatus();
        Volatile.Write(ref _status, status);
        _lastBytes = 0;
        SetPhase(phase);
        return status;
    }

    /// <summary>Offers a next step once done (callable from the work's background thread).</summary>
    public void OfferFollowUp(string text, Action action) => Volatile.Write(ref _followUp, new Offer(text, action));

    /// <summary>Starts it, or has it wait while another operation works on the same files.</summary>
    public Task RunAsync() => Run = RunCoreAsync();

    private async Task RunCoreAsync()
    {
        if (_work is not { } work)
            return;
        // Checked at every start, a resume too: the screen offers nothing that's busy, but several targets picked at once, a
        // resume or a dialog left open can still ask for it. It starts by itself once the other one ends.
        if (_owner.RunningOverlap(this) is { } other)
        {
            if (State != OperationState.Waiting)
                Turn = Interlocked.Increment(ref s_turns);
            State = OperationState.Waiting;
            Phase = Strings.PhaseWaiting;
            Message = Localized.Format(Strings.WaitsForOther, other.Subject, other.Action.Running);
            return;
        }

        _cancellation = new CancellationTokenSource();
        _status = new InstallStatus();
        _lastBytes = 0;
        _bytesPerSecond = 0;
        _speedClock.Restart();
        State = OperationState.Running;
        Message = null;
        _followUp = null;
        _owner.UpdateBusy();
        _timer.Start();

        try
        {
            Message = await Task.Run(() => work(this, _status, _cancellation.Token));
            State = OperationState.Completed;
            Phase = Strings.PhaseDone;
            _work = null; // nothing to retry
        }
        catch (OperationCanceledException)
        {
            State = OperationState.Cancelled;
            Phase = Strings.PhasePaused;
            Message = Strings.StoppedRetry;
        }
        catch (Exception ex)
        {
            State = OperationState.Failed;
            Phase = Strings.PhaseFailed;
            Message = ex.Message;
            if (ex is NotLoggedInException ended)
                _owner.SessionEnded(ended);
        }
        finally
        {
            _timer.Stop();
            UpdateProgress();
            SpeedText = "";
            _owner.UpdateBusy();
            _cancellation.Dispose();
            _cancellation = null;
            MemoryRelief.Release();
        }

        _owner.StartWaiting();
        if (State == OperationState.Completed)
        {
            _owner.MoveToHistory(this, OperationOutcome.Completed);
            await _owner.OnOperationCompletedAsync(this);
        }
    }

    /// <summary>Stops it, or keeps it from starting when its turn comes; Resume continues where it left off.</summary>
    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        if (IsWaiting)
        {
            State = OperationState.Cancelled;
            Phase = Strings.PhasePaused;
            Message = Strings.StoppedRetry;
        }
        else
        {
            _cancellation?.Cancel();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync() => RunAsync();

    /// <summary>
    /// Paused, failed or waiting, and not to be resumed: into the history. What it wrote stays; starting it anew continues from
    /// there.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveToHistory))]
    private void MoveToHistory()
    {
        _owner.MoveToHistory(this, IsFailed ? OperationOutcome.Failed : OperationOutcome.Paused);
        _work = null; // a failed install kept its manifest for retry
        MemoryRelief.Release();
    }

    private void UpdateProgress()
    {
        var status = Volatile.Read(ref _status);
        bool downloading = status.DownloadTotal > 0;
        long done = downloading ? status.DownloadedBytes : status.WrittenBytes;
        long total = downloading ? status.DownloadTotal : status.WriteTotal;

        IsIndeterminate = total == 0;
        Progress = total == 0 ? 0 : 100.0 * done / total;
        ProgressText = total == 0 ? "" : Localized.Format(Strings.ProgressOf, ByteSize.Format(done), ByteSize.Format(total)) +
            (status.FilesTotal > 0 ? "  ·  " + Localized.Plural(nameof(Strings.ProgressFiles_Other), status.FilesTotal, status.FilesDone) : "");

        double seconds = _speedClock.Elapsed.TotalSeconds;
        if (seconds >= 1 && IsRunning)
        {
            // Smoothed speed so the number doesn't jump every tick.
            double instant = (done - _lastBytes) / seconds;
            _bytesPerSecond = _bytesPerSecond == 0 ? instant : _bytesPerSecond * 0.7 + instant * 0.3;
            _lastBytes = done;
            _speedClock.Restart();

            string eta = _bytesPerSecond > 0 && total > done ? "  ·  " + Localized.Format(Strings.TimeLeft, FormatETA((total - done) / _bytesPerSecond)) : "";
            SpeedText = Localized.Format(Strings.PerSecond, ByteSize.Format((long)_bytesPerSecond)) + eta;
        }
    }

    private static string FormatETA(double seconds) => seconds switch
    {
        < 60 => Localized.Format(Strings.DurationSeconds, seconds),
        < 3600 => Localized.Format(Strings.DurationMinutes, seconds / 60),
        _ => Localized.Format(Strings.DurationHoursMinutes, (int)(seconds / 3600), (int)(seconds % 3600 / 60)),
    };
}
