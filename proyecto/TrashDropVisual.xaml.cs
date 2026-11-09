using System.Windows;
using System.Windows.Controls;

namespace _stick;

public partial class TrashDropVisual : UserControl
{
    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(
        nameof(IsHighlighted), typeof(bool), typeof(TrashDropVisual), new PropertyMetadata(false));

    public TrashDropVisual() => InitializeComponent();

    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }
}
