using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace _stick;

public partial class App : Application
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "_stick";
    private readonly LocalStore store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "notes.json"));
    private readonly DispatcherTimer scanTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Dictionary<Note, NoteWindow> openNotes = [];
    private Dictionary<string, HashSet<int>> running = new(StringComparer.OrdinalIgnoreCase);
    private ProcessMonitor? monitor;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private CollectionsWindow? settings;
    private Mutex? instance;
    private EventWaitHandle? openPanelSignal;
    private RegisteredWaitHandle? openPanelRegistration;
    private bool dirty;

    internal List<NoteCollection> Collections { get; private set; } = [];
    internal TrashLayout TrashLayout { get; private set; } = new();
    internal event EventHandler? TrashChanged;

    internal bool SetTrashLayout(TrashLayout layout)
    {
        try
        {
            var normalized = layout.Normalize();
            store.SaveTrashLayout(normalized);
            TrashLayout = normalized;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            NotifyError("No se pudieron guardar los ajustes de la papelera.", error);
            return false;
        }
    }

    internal RunningApplication[] FindOpenApplications() => monitor!.FindOpenApplications();

    internal bool HasSeenAppPickerTutorial() => store.HasSeenAppPickerTutorial();

    internal void MarkAppPickerTutorialSeen()
    {
        try
        {
            store.MarkAppPickerTutorialSeen();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            NotifyError("No se pudo guardar el estado del tutorial.", error);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        using var identity = WindowsIdentity.GetCurrent();
        var userId = identity.User?.Value;
        openPanelSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\_stick-open-{userId}");
        instance = new Mutex(false, $@"Global\_stick-{userId}", out var firstInstance);
        if (!firstInstance)
        {
            openPanelSignal.Set();
            Shutdown();
            return;
        }

        try
        {
            Collections = store.Load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show($"No se pudo leer el archivo de notas.\n\n{error.Message}", AppName,
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        CreateTray();
        try
        {
            TrashLayout = store.LoadTrashLayout();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            NotifyError("Se usará la configuración predeterminada de la papelera.", error);
        }

        saveTimer.Tick += (_, _) => Flush();
        EnsureBuiltInCollection();
        monitor = new ProcessMonitor();
        scanTimer.Tick += (_, _) => Scan();
        Scan();
        scanTimer.Start();
        OpenSettings(focusWelcome: true);
        openPanelRegistration = ThreadPool.RegisterWaitForSingleObject(openPanelSignal,
            (_, _) => QueueOpenSettings(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Flush();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        scanTimer.Stop();
        Flush();
        tray?.Dispose();
        trayIcon?.Dispose();
        openPanelRegistration?.Unregister(null);
        openPanelSignal?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }

    private NoteCollection? BuiltInCollection => Collections.FirstOrDefault(collection => collection.IsBuiltIn);

    private void EnsureBuiltInCollection()
    {
        if (BuiltInCollection is not null)
            return;

        Collections.Insert(0, new NoteCollection
        {
            Executable = "",
            IsBuiltIn = true,
            Automatic = false,
            Notes = [new Note { IsWelcome = true, Color = "#FFE275" }]
        });

        if (!TrySave())
            dirty = true;
    }

    internal NoteCollection? AddCollection(string path)
    {
        if (!ValidExecutable(path))
            return null;

        var collection = new NoteCollection { Executable = path };
        Collections.Add(collection);
        if (TrySave())
            return collection;

        Collections.Remove(collection);
        return null;
    }

    internal bool RemoveCollection(NoteCollection collection)
    {
        if (collection.IsBuiltIn)
            return false;

        var index = Collections.IndexOf(collection);
        if (index < 0)
            return false;

        Collections.RemoveAt(index);
        if (!TrySave())
        {
            Collections.Insert(index, collection);
            return false;
        }

        running.Remove(collection.Executable);
        CloseCollection(collection);
        return true;
    }

    internal bool ReplaceExecutable(NoteCollection collection, string path)
    {
        if (collection.IsBuiltIn)
            return false;

        var oldPath = collection.Executable;
        if (StringComparer.OrdinalIgnoreCase.Equals(oldPath, path))
            return true;
        if (!ValidExecutable(path))
            return false;

        collection.Executable = path;
        if (!TrySave())
        {
            collection.Executable = oldPath;
            return false;
        }

        running.Remove(oldPath);
        UpdateCollectionTitles(collection);
        return true;
    }

    internal bool RenameCollection(NoteCollection collection, string name)
    {
        if (collection.IsBuiltIn)
            return false;

        var oldName = collection.CustomName;
        var newName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (oldName == newName)
            return true;

        collection.CustomName = newName;
        if (!TrySave())
        {
            collection.CustomName = oldName;
            return false;
        }

        UpdateCollectionTitles(collection);
        return true;
    }

    private void UpdateCollectionTitles(NoteCollection collection)
    {
        foreach (var note in collection.Notes)
            if (openNotes.TryGetValue(note, out var window))
                window.Title = collection.Name;
    }

    internal bool ToggleAutomatic(NoteCollection collection)
    {
        if (collection.IsBuiltIn)
            return false;

        collection.Automatic = !collection.Automatic;
        if (!TrySave())
        {
            collection.Automatic = !collection.Automatic;
            return false;
        }

        running.Remove(collection.Executable);
        return true;
    }

    internal void OpenCollection(NoteCollection collection, bool activate = false)
    {
        if (collection.Notes.Count == 0)
        {
            AddNote(collection, activate);
            return;
        }

        foreach (var note in collection.Notes)
            ShowNote(collection, note, activate: false);

        if (activate)
            openNotes[collection.Notes[0]].Activate();
    }

    internal bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) is string command
                && StringComparer.OrdinalIgnoreCase.Equals(command, StartupCommand);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            NotifyError("No se pudo consultar el inicio con Windows.", error);
            return false;
        }
    }

    internal bool SetStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                if (StartupCommand.Length > 260)
                    throw new IOException("La ruta de _stick es demasiado larga para el inicio automático.");
                key.SetValue(AppName, StartupCommand);
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            MessageBox.Show($"No se pudo cambiar el inicio con Windows.\n\n{error.Message}", AppName,
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static string StartupCommand => $"\"{Environment.ProcessPath}\" --background";

    private void CreateTray()
    {
        using var iconStream = GetResourceStream(new Uri("/_stick;component/Assets/stick.ico", UriKind.Relative)).Stream;
        trayIcon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize);
        tray = new Forms.NotifyIcon
        {
            Icon = trayIcon,
            Text = AppName,
            Visible = true
        };
        tray.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                QueueOpenSettings();
        };
    }

    private void QueueOpenSettings() =>
        Dispatcher.InvokeAsync(() => OpenSettings(), DispatcherPriority.Normal);

    internal void RequestExit()
    {
        if (Flush())
            Shutdown();
    }

    private void OpenSettings(bool focusWelcome = false)
    {
        if (settings is not null)
        {
            settings.Activate();
            return;
        }

        var area = SystemParameters.WorkArea;
        var panel = new CollectionsWindow(this);
        panel.Width = Math.Min(panel.Width, area.Width - 24);
        panel.Height = Math.Min(panel.Height, area.Height - 24);
        panel.Left = area.Right - panel.Width - 12;
        panel.Top = area.Bottom - panel.Height - 12;
        settings = panel;
        panel.Closed += (_, _) => settings = null;

        panel.Show();
        if (BuiltInCollection is { } builtIn)
            foreach (var note in builtIn.Notes)
                ShowNote(builtIn, note, activate: false);

        var welcome = focusWelcome ? BuiltInCollection?.Notes.FirstOrDefault(note => note.IsWelcome) : null;
        if (welcome is not null && openNotes.TryGetValue(welcome, out var welcomeWindow))
            welcomeWindow.Activate();
        else
            panel.Activate();
    }

    private void Scan()
    {
        var enabled = Collections.Where(collection => !collection.IsBuiltIn && collection.Automatic).ToArray();
        Dictionary<string, HashSet<int>> current;
        try
        {
            current = monitor!.FindRunning(enabled.Select(collection => collection.Executable));
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine(error);
            return;
        }

        var previous = running;
        running = current;
        foreach (var collection in enabled)
        {
            previous.TryGetValue(collection.Executable, out var previousIds);
            if (current.TryGetValue(collection.Executable, out var ids))
            {
                if (previousIds is null || !ids.Overlaps(previousIds))
                    OpenCollection(collection);
            }
            else if (previousIds is not null)
            {
                CloseCollection(collection);
            }
        }
    }

    private void CloseCollection(NoteCollection collection)
    {
        foreach (var note in collection.Notes)
            if (openNotes.TryGetValue(note, out var window))
                window.Close();
    }

    private void AddNote(NoteCollection collection, bool activate)
    {
        var note = new Note();
        collection.Notes.Add(note);
        if (TrySave())
            ShowNote(collection, note, activate);
        else
            collection.Notes.Remove(note);
    }

    private void DeleteNote(NoteCollection collection, Note note)
    {
        var index = collection.Notes.IndexOf(note);
        if (index < 0)
            return;

        collection.Notes.RemoveAt(index);
        collection.TrashedNotes.Add(note);
        if (!TrySave())
        {
            collection.TrashedNotes.RemoveAt(collection.TrashedNotes.Count - 1);
            collection.Notes.Insert(index, note);
            return;
        }

        if (openNotes.TryGetValue(note, out var window))
            window.Close();

        TrashChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void RestoreLastNote(NoteCollection collection)
    {
        if (collection.TrashedNotes.Count == 0)
            return;

        var note = collection.TrashedNotes[^1];
        collection.TrashedNotes.RemoveAt(collection.TrashedNotes.Count - 1);
        collection.Notes.Add(note);
        if (!TrySave())
        {
            collection.Notes.Remove(note);
            collection.TrashedNotes.Add(note);
            return;
        }

        ShowNote(collection, note, activate: true);
        TrashChanged?.Invoke(this, EventArgs.Empty);
    }

    private NoteWindow[] GetOpenNotes(NoteCollection collection) =>
        collection.Notes
            .Where(openNotes.ContainsKey)
            .Select(note => openNotes[note])
            .ToArray();

    private void ShowNote(NoteCollection collection, Note note, bool activate)
    {
        if (!openNotes.TryGetValue(note, out var window))
        {
            window = new NoteWindow(collection.Name, new NoteViewModel(note, QueueSave),
                () => AddNote(collection, activate: true), () => DeleteNote(collection, note),
                () => GetOpenNotes(collection), () => TrashLayout);
            PlaceNote(window, note.IsWelcome);
            window.Closed += (_, _) =>
            {
                openNotes.Remove(note);
                Flush();
            };
            openNotes.Add(note, window);
            window.Show();
        }

        if (activate)
            window.Activate();
    }

    private void PlaceNote(NoteWindow window, bool isWelcome)
    {
        var area = SystemParameters.WorkArea;
        if (isWelcome)
        {
            window.Left = area.Left + (area.Width - window.Width) / 2;
            window.Top = area.Top + (area.Height - window.Height) / 2;
            return;
        }

        var rows = Math.Max(1, (int)((area.Height - 24) / (window.Height + 10)));
        var columns = Math.Max(1, (int)((area.Width - 24) / (window.Width + 10)));

        for (var index = 0; index < rows * columns; index++)
        {
            var left = Math.Max(area.Left, area.Right - 12 - window.Width - (index / rows) * (window.Width + 10));
            var top = Math.Min(area.Top + 12 + (index % rows) * (window.Height + 10), area.Bottom - window.Height);
            var bounds = new Rect(left, top, window.Width, window.Height);
            if ((settings is { IsVisible: true } openPanel
                 && bounds.IntersectsWith(new Rect(openPanel.Left, openPanel.Top, openPanel.Width, openPanel.Height)))
                || openNotes.Values.Any(note => bounds.IntersectsWith(new Rect(note.Left, note.Top, note.Width, note.Height))))
                continue;

            window.Left = left;
            window.Top = top;
            return;
        }

        var offset = (openNotes.Count % 8) * 12;
        window.Left = Math.Max(area.Left, area.Right - window.Width - 12 - offset);
        window.Top = Math.Min(area.Top + 12 + offset, area.Bottom - window.Height);
    }

    private bool ValidExecutable(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Environment.ProcessPath, path, StringComparison.OrdinalIgnoreCase)
            && !Collections.Any(collection => string.Equals(collection.Executable, path,
                StringComparison.OrdinalIgnoreCase)))
            return true;

        MessageBox.Show("Elige un ejecutable distinto de _stick que aún no esté vinculado.", AppName);
        return false;
    }

    private void QueueSave()
    {
        dirty = true;
        saveTimer.Stop();
        saveTimer.Start();
    }

    private bool Flush()
    {
        saveTimer.Stop();
        return !dirty || TrySave();
    }

    private bool TrySave()
    {
        try
        {
            store.Save(Collections);
            dirty = false;
            saveTimer.Stop();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            NotifyError("No se pudieron guardar las notas.", error);
            return false;
        }
    }

    private void NotifyError(string title, Exception error) =>
        tray?.ShowBalloonTip(5000, title, error.Message, Forms.ToolTipIcon.Error);
}
