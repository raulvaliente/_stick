using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace _stick;

public partial class AppPickerWindow : Window
{
    private readonly App app;
    private readonly string? currentExecutable;
    private readonly HashSet<string> linkedPaths;

    internal AppPickerWindow(App app, string? currentExecutable)
    {
        this.app = app;
        this.currentExecutable = currentExecutable;
        linkedPaths = app.Collections.Select(collection => collection.Executable)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (currentExecutable is not null)
            linkedPaths.Remove(currentExecutable);

        InitializeComponent();
        if (currentExecutable is not null)
        {
            Title = "Cambiar aplicación vinculada";
            Introduction.Text = "Selecciona el programa nuevo para esta biblioteca. Sus notas se conservan.";
        }

        if (!app.HasSeenAppPickerTutorial())
            Tutorial.Visibility = Visibility.Visible;

        Loaded += async (_, _) => await RefreshApplicationsAsync();
    }

    internal string? SelectedExecutable { get; private set; }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshApplicationsAsync();

    private async Task RefreshApplicationsAsync()
    {
        RefreshButton.IsEnabled = false;
        var previous = (Applications.SelectedItem as RunningApplication)?.Executable ?? currentExecutable;
        Applications.ItemsSource = null;
        Status.Text = "Buscando aplicaciones abiertas…";
        try
        {
            var running = await Task.Run(app.FindOpenApplications);
            if (!IsVisible)
                return;

            var choices = running.Where(application => !linkedPaths.Contains(application.Executable)).ToArray();
            Applications.ItemsSource = choices;
            Applications.SelectedItem = choices.FirstOrDefault(application =>
                StringComparer.OrdinalIgnoreCase.Equals(application.Executable, previous));
            Status.Text = choices.Length == 0
                ? "No hay aplicaciones abiertas disponibles. Abre una y pulsa Actualizar lista."
                : "";
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            Status.Text = "No se pudo cargar la lista. Pulsa Actualizar lista o Buscar archivo…";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e) =>
        new ExecutableHelpWindow { Owner = this }.ShowDialog();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona el ejecutable de la aplicación",
            Filter = "Aplicaciones (*.exe)|*.exe"
        };

        if (dialog.ShowDialog(this) == true)
            SelectExecutable(dialog.FileName);
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (Applications.SelectedItem is RunningApplication application)
            SelectExecutable(application.Executable);
    }

    private void SelectExecutable(string path)
    {
        if (linkedPaths.Contains(path))
        {
            MessageBox.Show(this, "Esa aplicación ya está vinculada a otra biblioteca.", "_stick");
            return;
        }

        SelectedExecutable = path;
        DismissTutorial();
        DialogResult = true;
    }

    private void DismissTutorial_Click(object sender, RoutedEventArgs e) => DismissTutorial();

    private void DismissTutorial()
    {
        if (Tutorial.Visibility != Visibility.Visible)
            return;

        app.MarkAppPickerTutorialSeen();
        Tutorial.Visibility = Visibility.Collapsed;
    }
}
