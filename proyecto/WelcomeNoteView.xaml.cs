using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace _stick;

public partial class WelcomeNoteView : UserControl
{
    public WelcomeNoteView() => InitializeComponent();

    private void GrabFrame_MouseEnter(object sender, MouseEventArgs e) => Play("GrabMotion");

    private void DragFrame_MouseEnter(object sender, MouseEventArgs e) => Play("DragMotion");

    private void DropFrame_MouseEnter(object sender, MouseEventArgs e) => Play("DropMotion");

    private void Play(string key)
    {
        if (SystemParameters.ClientAreaAnimation)
            ((Storyboard)Resources[key]).Begin(this);
    }
}
