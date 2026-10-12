using Avalonia;
using Avalonia.Controls;

namespace UnVault.App.Views;

/// <summary>
/// The dot on a tab: yellow while something there wants a look; blue and pulsing while work runs (the Downloads tab). What
/// wants a look comes first: it's waiting for the user, the work isn't.
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

    /// <summary>What the dot shows now: (blue and pulsing, yellow).</summary>
    internal (bool Working, bool Attention) Shown => (Dot.Classes.Contains("working"), Dot.Classes.Contains("attention"));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsWorkingProperty || change.Property == NeedsAttentionProperty)
            Show();
    }

    private void Show()
    {
        // Pulsing only while shown and blue: the animation runs while the class is on.
        Dot.Classes.Set("attention", NeedsAttention);
        Dot.Classes.Set("working", IsWorking && !NeedsAttention);
        Dot.IsVisible = IsWorking || NeedsAttention;
    }
}
