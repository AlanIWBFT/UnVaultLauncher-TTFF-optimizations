using Avalonia.Controls;
using UnVault.App.ViewModels;

namespace UnVault.App.Views;

public partial class ProjectCardView : UserControl
{
    public ProjectCardView() => InitializeComponent();

    /// <summary>
    /// The Categories submenu, made as the menu opens so it lists the categories as they are now: each one, ticked when the
    /// project is in it (clicking puts it in or takes it out), then New Category.
    /// </summary>
    internal void OnMoreOpening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout { Items: [MenuItem categories, ..] } || DataContext is not ProjectCardViewModel card)
            return;
        categories.Items.Clear();
        foreach (var category in card.Categories)
        {
            categories.Items.Add(new MenuItem
            {
                Header = category.Name.Replace("_", "__"), // a single _ would mark an access key, and vanish
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = card.IsIn(category),
                Command = card.ToggleCategoryCommand,
                CommandParameter = category,
            });
        }
        if (categories.Items.Count > 0)
            categories.Items.Add(new Separator());
        categories.Items.Add(new MenuItem { Header = Strings.NewCategoryEllipsis, Command = card.NewCategoryCommand });
    }
}
