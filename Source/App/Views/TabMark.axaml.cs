using Avalonia;
using Avalonia.Controls;

namespace UnVault.App.Views;

/// <summary>
/// The line under a tab: blue and moving while work runs (the Downloads tab), yellow while something there wants a look.
/// Work comes first; the yellow shows once it stops.
/// </summary>
public partial class TabMark : UserControl
{
    public static readonly StyledProperty<bool> IsWorkingProperty = AvaloniaProperty.Register<TabMark, bool>(nameof(IsWorking));
    public static readonly StyledProperty<bool> NeedsAttentionProperty = AvaloniaProperty.Register<TabMark, bool>(nameof(NeedsAttention));

    public TabMark()
    {
        InitializeComponent();
        Show();
    }

    public bool IsWorking
    {
        get => GetValue(IsWorkingProperty);
        set => SetValue(IsWorkingProperty, value);
    }

    public bool NeedsAttention
    {
        get => GetValue(NeedsAttentionProperty);
        set => SetValue(NeedsAttentionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsWorkingProperty || change.Property == NeedsAttentionProperty)
            Show();
    }

    private void Show()
    {
        // Moving only while shown: a hidden bar's animation would keep running unseen.
        WorkingBar.IsVisible = WorkingBar.IsIndeterminate = IsWorking;
        AttentionLine.IsVisible = NeedsAttention && !IsWorking;
    }
}
