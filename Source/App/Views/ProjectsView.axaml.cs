using Avalonia.Controls;

namespace UnVault.App.Views;

public partial class ProjectsView : UserControl
{
    /// <summary>
    /// The narrowest a card may be, so a column fits the picture, room for the name and folder, and both buttons. Two
    /// columns need twice that (plus the gap), so a window near its narrowest shows one.
    /// </summary>
    public const double MinColumnWidth = 500;

    public ProjectsView() => InitializeComponent();
}
