using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        Collections.SelectedIndex = 0;
        Startup.IsChecked = app.IsStartupEnabled();
        TaskbarIcon.IsChecked = app.ShowTaskbarIcon;
        ShowInTaskbar = app.ShowTaskbarIcon;
        app.TrashChanged += OnTrashChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private NoteCollection? Selected => Collections.SelectedItem as NoteCollection;
    public bool HasSelection => Selected is not null;
    public bool CanRestore => Selected?.TrashedNotes.Count > 0;
    public bool HasExternalSelection => Selected is { IsBuiltIn: false };
    public string AutomaticAction => Selected?.Automatic == false ? "Activar detección" : "Pausar detección";

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && ShowInTaskbar && !app.IsExiting)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        app.TrashChanged -= OnTrashChanged;
        base.OnClosed(e);
    }

    private void OnTrashChanged(object? sender, EventArgs e) =>
        PropertyChanged?.Invoke(this, new(nameof(CanRestore)));

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } collection)
            app.RestoreLastNote(collection);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => app.RequestExit();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (PickExecutable() is string path && app.AddCollection(path) is NoteCollection collection)
            Refresh(collection);
    }

    private void DeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { IsBuiltIn: false } collection)
            return;

        var message = $"¿Borrar la biblioteca «{collection.Name}»?\n\n"
            + "Se desvinculará la aplicación y se borrarán todas sus notas, incluidas las de la papelera. "
            + "Esta acción es permanente.";
        if (MessageBox.Show(this, message, "Borrar biblioteca", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var selectedIndex = Collections.SelectedIndex;
        if (app.RemoveCollection(collection))
        {
            Collections.Items.Refresh();
            Collections.SelectedIndex = Math.Min(selectedIndex, Collections.Items.Count - 1);
        }
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is NoteCollection collection && PickExecutable(collection.Executable) is string path
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

    private void TaskbarIcon_Click(object sender, RoutedEventArgs e)
    {
        var visible = TaskbarIcon.IsChecked == true;
        if (app.SetTaskbarIcon(visible))
            ShowInTaskbar = visible;
        else
            TaskbarIcon.IsChecked = app.ShowTaskbarIcon;
    }

    private void TrashSettings_Click(object sender, RoutedEventArgs e) =>
        new TrashSettingsWindow(app) { Owner = this }.ShowDialog();

    private string? PickExecutable(string? currentExecutable = null)
    {
        var dialog = new AppPickerWindow(app, currentExecutable) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.SelectedExecutable : null;
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
        PropertyChanged?.Invoke(this, new(nameof(CanRestore)));
        PropertyChanged?.Invoke(this, new(nameof(HasExternalSelection)));
        PropertyChanged?.Invoke(this, new(nameof(AutomaticAction)));
    }
}
