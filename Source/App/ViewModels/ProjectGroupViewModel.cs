using CommunityToolkit.Mvvm.Input;
using UnVault.App.Services;

namespace UnVault.App.ViewModels;

/// <summary>
/// A bundle of projects on the Projects tab, under a heading that folds it: one of the user's categories, or the projects
/// in none of them. With no categories at all, the one group is every project, without a heading.
/// </summary>
public partial class ProjectGroupViewModel(ProjectsViewModel owner, ProjectCategory? category, IReadOnlyList<ProjectCardViewModel> items,
    bool hasHeading, bool searching) : ViewModelBase
{
    /// <summary>The user's category; null for the projects in none.</summary>
    public ProjectCategory? Category { get; } = category;

    public IReadOnlyList<ProjectCardViewModel> Items { get; } = items;

    public bool HasHeading { get; } = hasHeading;

    public string Title => Category?.Name ?? Strings.Uncategorized;

    /// <summary>How many projects it has, shown after its name.</summary>
    public string CountText => Items.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    public bool IsFolded => Category?.Folded ?? owner.Categories.UncategorizedFolded;

    /// <summary>Its projects shown: unfolded, or while searching, so a match is never hidden.</summary>
    public bool IsOpen => !IsFolded || searching;

    /// <summary>A category with no projects yet says how to add some (not while searching: then it isn't shown at all).</summary>
    public bool ShowsEmptyHint => IsOpen && Items.Count == 0;

    /// <summary>Only the user's own categories can be renamed or deleted.</summary>
    public bool CanEdit => Category is not null;

    [RelayCommand]
    private void ToggleFold() => owner.ToggleFold(this);

    [RelayCommand]
    private void Rename() => owner.AskRename(this);

    [RelayCommand]
    private void Delete() => owner.AskDelete(this);
}
