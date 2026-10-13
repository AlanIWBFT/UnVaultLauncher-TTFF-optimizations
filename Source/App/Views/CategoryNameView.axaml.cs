using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UnVault.App.Views;

public partial class CategoryNameView : UserControl
{
    public CategoryNameView() => InitializeComponent();

    /// <summary>
    /// Into the name, with what's there selected: after the window has focused the dialog itself (it does so for every
    /// dialog), so that comes later.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, DispatcherPriority.Background);
    }
}
