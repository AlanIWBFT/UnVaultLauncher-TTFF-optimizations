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

    /// <summary>How many projects it has, shown after its name: "(4)".</summary>
    public string CountText => Localized.Format(Strings.ProjectGroupCount, Items.Count);

    /// <summary>Only with a heading to unfold it by: without categories, the one list of every project never is.</summary>
    public bool IsFolded => HasHeading && (Category?.Folded ?? owner.Categories.UncategorizedFolded);

    /// <summary>Its projects shown: unfolded, or while searching, so a match is never hidden.</summary>
    public bool IsOpen => !IsFolded || searching;

    /// <summary>A category with no projects yet says how to add some (not while searching: then it isn't shown at all).</summary>
    public bool ShowsEmptyHint => Items.Count == 0;

    /// <summary>Only the user's own categories can be renamed or deleted.</summary>
    public bool CanEdit => Category is not null;

    [RelayCommand]
    private void ToggleFold() => owner.ToggleFold(this);

    /// <summary>Folded or unfolded by its heading: shown on this group as it is, so its projects can slide, not built anew.</summary>
    internal void FoldChanged()
    {
        OnPropertyChanged(nameof(IsFolded));
        OnPropertyChanged(nameof(IsOpen));
    }

    [RelayCommand]
    private void Rename() => owner.AskRename(this);

    [RelayCommand]
    private void Delete() => owner.AskDelete(this);
}
