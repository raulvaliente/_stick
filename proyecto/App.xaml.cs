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
    private Forms.ContextMenuStrip? menu;
    private System.Drawing.Font? trayFont;
    private CollectionsWindow? settings;
    private Mutex? instance;
    private bool dirty;

    internal List<NoteCollection> Collections { get; private set; } = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        using var identity = WindowsIdentity.GetCurrent();
        instance = new Mutex(false, $@"Global\_stick-{identity.User?.Value}", out var firstInstance);
        if (!firstInstance)
        {
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
        saveTimer.Tick += (_, _) => Flush();
        monitor = new ProcessMonitor();
        scanTimer.Tick += (_, _) => Scan();
        Scan();
        scanTimer.Start();

        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            OpenSettings();
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
        menu?.Dispose();
        trayFont?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
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

    internal bool ReplaceExecutable(NoteCollection collection, string path)
    {
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
        trayFont = new System.Drawing.Font("Consolas", 9);
        menu = new Forms.ContextMenuStrip { Font = trayFont };
        menu.Items.Add("Aplicaciones…", null, (_, _) => OpenSettings());
        var openMenu = new Forms.ToolStripMenuItem("Abrir notas");
        openMenu.DropDown.Font = trayFont;
        menu.Items.Add(openMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir de _stick", null, (_, _) =>
        {
            if (Flush())
                Shutdown();
        });
        menu.Opening += (_, _) =>
        {
            while (openMenu.DropDownItems.Count > 0)
                openMenu.DropDownItems[0].Dispose();

            foreach (var collection in Collections)
                openMenu.DropDownItems.Add(collection.Name.Replace("&", "&&"), null,
                    (_, _) => OpenCollection(collection, activate: true));
            openMenu.Enabled = openMenu.DropDownItems.Count > 0;
        };
        using var iconStream = GetResourceStream(new Uri("/_stick;component/Assets/stick.ico", UriKind.Relative)).Stream;
        trayIcon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize);
        tray = new Forms.NotifyIcon
        {
            Icon = trayIcon,
            Text = AppName,
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => OpenSettings();
    }

    private void OpenSettings()
    {
        if (settings is null)
        {
            settings = new CollectionsWindow(this);
            settings.Closed += (_, _) => settings = null;
            settings.Show();
        }

        if (settings.WindowState == WindowState.Minimized)
            settings.WindowState = WindowState.Normal;
        settings.Activate();
    }

    private void Scan()
    {
        var enabled = Collections.Where(collection => collection.Automatic).ToArray();
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
        if (!TrySave())
        {
            collection.Notes.Insert(index, note);
            return;
        }

        if (openNotes.TryGetValue(note, out var window))
            window.Close();
    }

    private void ShowNote(NoteCollection collection, Note note, bool activate)
    {
        if (!openNotes.TryGetValue(note, out var window))
        {
            window = new NoteWindow(collection.Name, new NoteViewModel(note, QueueSave),
                () => AddNote(collection, activate: true), () => DeleteNote(collection, note));
            PlaceNote(window);
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

    private void PlaceNote(NoteWindow window)
    {
        var area = SystemParameters.WorkArea;
        var rows = Math.Max(1, (int)((area.Height - 24) / (window.Height + 10)));
        var columns = Math.Max(1, (int)((area.Width - 24) / (window.Width + 10)));

        for (var index = 0; index < rows * columns; index++)
        {
            var left = Math.Max(area.Left, area.Right - 12 - window.Width - (index / rows) * (window.Width + 10));
            var top = Math.Min(area.Top + 12 + (index % rows) * (window.Height + 10), area.Bottom - window.Height);
            var bounds = new Rect(left, top, window.Width, window.Height);
            if (openNotes.Values.Any(note => bounds.IntersectsWith(new Rect(note.Left, note.Top, note.Width, note.Height))))
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
