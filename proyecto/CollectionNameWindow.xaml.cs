using System.Windows;

namespace _stick;

public partial class CollectionNameWindow : Window
{
    internal CollectionNameWindow(string? currentName, string defaultName)
    {
        InitializeComponent();
        NameInput.Text = currentName ?? "";
        DefaultNameHint.Text = $"Si lo dejas vacío, se usará «{defaultName}».";
        Loaded += (_, _) =>
        {
            NameInput.Focus();
            NameInput.SelectAll();
        };
    }

    internal string CollectionName => NameInput.Text;

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
