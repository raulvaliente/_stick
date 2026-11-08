using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace _stick;

public partial class CollectionsWindow : Window, INotifyPropertyChanged
{
    private readonly App app;

    internal CollectionsWindow(App app)
    {
        this.app = app;
        InitializeComponent();
        DataContext = this;
        Collections.ItemsSource = app.Collections;
        Startup.IsChecked = app.IsStartupEnabled();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private NoteCollection? Selected => Collections.SelectedItem as NoteCollection;
    public bool HasSelection => Selected is not null;
    public string AutomaticAction => Selected?.Automatic == false ? "Activar detección" : "Pausar detección";

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (PickExecutable() is string path && app.AddCollection(path) is NoteCollection collection)
            Refresh(collection);
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is NoteCollection collection && PickExecutable() is string path
            && app.ReplaceExecutable(collection, path))
            Refresh(collection);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not NoteCollection collection)
            return;

        var dialog = new CollectionNameWindow(
            collection.CustomName, Path.GetFileNameWithoutExtension(collection.Executable)) { Owner = this };
        if (dialog.ShowDialog() == true && app.RenameCollection(collection, dialog.CollectionName))
            Refresh(collection);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is NoteCollection collection)
            app.OpenCollection(collection, activate: true);
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is NoteCollection collection && app.ToggleAutomatic(collection))
            Refresh(collection);
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        var enabled = Startup.IsChecked == true;
        if (!app.SetStartup(enabled))
            Startup.IsChecked = !enabled;
    }

    private void Help_Click(object sender, RoutedEventArgs e) =>
        new ExecutableHelpWindow { Owner = this }.ShowDialog();

    private string? PickExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona el ejecutable de la aplicación",
            Filter = "Aplicaciones (*.exe)|*.exe"
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void Refresh(NoteCollection collection)
    {
        Collections.Items.Refresh();
        Collections.SelectedItem = collection;
        UpdateSelection();
    }

    private void Collections_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();

    private void UpdateSelection()
    {
        PropertyChanged?.Invoke(this, new(nameof(HasSelection)));
        PropertyChanged?.Invoke(this, new(nameof(AutomaticAction)));
    }
}
