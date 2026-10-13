using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UnVault.App.ViewModels;

/// <summary>Asks for a category's name: a new one, or a new name for one. No two categories may share a name.</summary>
public partial class CategoryNameViewModel(MainViewModel owner, string title, string confirmText, string name, Func<string, bool> isTaken,
    Action<string> onConfirm) : ViewModelBase
{
    public string Title { get; } = title;
    public string ConfirmText { get; } = confirmText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string Name { get; set; } = name;

    /// <summary>Why the name can't be used; null when it can, or while it's still empty.</summary>
    public string? Error => isTaken(Name.Trim()) ? Localized.Format(Strings.CategoryNameTaken, Name.Trim()) : null;

    private bool CanConfirm => Name.Trim().Length > 0 && Error is null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        owner.Close(this);
        onConfirm(Name.Trim());
    }

    [RelayCommand]
    private void Cancel() => owner.Close(this);
}
