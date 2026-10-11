using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UnVault.App.Services;

namespace UnVault.App.ViewModels;

/// <summary>
/// A finished operation in the Downloads tab's history: what it worked on, and beneath it what happened, in the past tense
/// ("Installed into UE 5.6  ·  E:\Epic Games\UE_5.6", "Install into UE 5.6 paused").
/// </summary>
public partial class HistoryEntryViewModel(OperationRecord record) : ViewModelBase
{
    public OperationRecord Record { get; } = record;

    /// <summary>The engine or Library item; a whole sentence in entries saved before operations kept their parts.</summary>
    public string Name => Record.Title;

    public string? Line => Record.Kind is { } kind ? Describe(new OperationAction(kind, Record.Target)) : Record.Message;
    public bool HasLine => !string.IsNullOrEmpty(Line);

    private string Describe(OperationAction action) => Record.Outcome switch
    {
        OperationOutcome.Completed => Record.Message is { Length: > 0 } detail ? $"{action.Done}  ·  {detail}" : action.Done,
        OperationOutcome.Paused => Localized.Format(Strings.ActionPaused, action.Name),
        _ => Localized.Format(Strings.ActionFailed, action.Name, Record.Message ?? ""),
    };

    public bool IsCompleted => Record.Outcome == OperationOutcome.Completed;
    public bool IsPaused => Record.Outcome == OperationOutcome.Paused;
    public bool IsFailed => Record.Outcome == OperationOutcome.Failed;

    /// <summary>The day it finished, which its group says.</summary>
    public DateTime Day => Record.FinishedAt.ToLocalTime().Date;

    public string TimeText => Record.FinishedAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    /// <summary>A next step the operation offered, e.g. Repair after a verify. This session only: it isn't saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFollowUp))]
    public partial string? FollowUpText { get; set; }

    public Action? FollowUp { get; set; }
    public bool HasFollowUp => FollowUpText is not null;

    /// <summary>Taken once: it starts a new operation of its own.</summary>
    [RelayCommand]
    private void RunFollowUp()
    {
        var followUp = FollowUp;
        FollowUp = null;
        FollowUpText = null;
        followUp?.Invoke();
    }
}

/// <summary>The history entries of one day, under a heading: Today, Yesterday, the weekday, or the date.</summary>
public sealed class HistoryDayViewModel(string heading, IReadOnlyList<HistoryEntryViewModel> entries)
{
    public string Heading { get; } = heading;
    public IReadOnlyList<HistoryEntryViewModel> Entries { get; } = entries;

    /// <summary>Groups entries (newest first) by the day they finished.</summary>
    public static List<HistoryDayViewModel> Group(IEnumerable<HistoryEntryViewModel> entries, DateTime today) =>
        [.. entries.GroupBy(e => e.Day).Select(day => new HistoryDayViewModel(HeadingFor(day.Key, today), [.. day]))];

    /// <summary>
    /// Weekday and month names in the language the app's texts are in, beside "Today" and "Yesterday": Windows' regional
    /// format can be another language (Russian dates under an English interface).
    /// </summary>
    internal static string HeadingFor(DateTime day, DateTime today)
    {
        var language = Strings.Culture ?? CultureInfo.CurrentUICulture;
        return day == today ? Strings.HistoryToday
            : day == today.AddDays(-1) ? Strings.HistoryYesterday
            : day > today.AddDays(-7) ? day.ToString("dddd", language) // this week: the weekday
            : day.Year == today.Year ? day.ToString("M", language) // "October 8", as the language puts it
            : day.ToString("D", language);
    }
}
