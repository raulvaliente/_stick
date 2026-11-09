using System.Windows;

namespace _stick;

public partial class TrashDropWindow : Window
{
    internal TrashDropWindow() => InitializeComponent();

    internal bool Contains(Point screenPoint)
    {
        var local = PointFromScreen(screenPoint);
        return new Rect(0, 0, ActualWidth, ActualHeight).Contains(local);
    }

    internal void Highlight(bool hovered) => DropTarget.IsHighlighted = hovered;
}
