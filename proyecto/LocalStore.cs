using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace _stick;

internal sealed class LocalStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private string AppPickerTutorialMarker => Path.Combine(Path.GetDirectoryName(filePath)!, "app-picker-tutorial.seen");
    private string TrashLayoutPath => Path.Combine(Path.GetDirectoryName(filePath)!, "trash-layout.json");

    public bool HasSeenAppPickerTutorial() => File.Exists(AppPickerTutorialMarker);

    public void MarkAppPickerTutorialSeen()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        using var marker = File.Create(AppPickerTutorialMarker);
    }

    public List<NoteCollection> Load()
    {
        FileStream file;
        try
        {
            file = File.OpenRead(filePath);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }

        using var openedFile = file;
        var collections = JsonSerializer.Deserialize<List<NoteCollection>>(file, Options);
        if (collections is null || collections.Any(collection =>
                collection is null || (!collection.IsBuiltIn && string.IsNullOrWhiteSpace(collection.Executable))
                || collection.Notes is null || collection.Notes.Any(note => note is null)
                || collection.TrashedNotes is null || collection.TrashedNotes.Any(note => note is null)))
            throw new JsonException("El archivo de notas no tiene el formato esperado.");

        return collections;
    }

    public TrashLayout LoadTrashLayout()
    {
        try
        {
            using var file = File.OpenRead(TrashLayoutPath);
            return (JsonSerializer.Deserialize<TrashLayout>(file, Options) ?? new TrashLayout()).Normalize();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new TrashLayout();
        }
    }

    public void SaveTrashLayout(TrashLayout layout) => SaveJson(TrashLayoutPath, layout);

    public void Save(List<NoteCollection> collections) => SaveJson(filePath, collections);

    private static void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryFile = path + ".tmp";
        using (var file = File.Create(temporaryFile))
        {
            JsonSerializer.Serialize(file, value, Options);
            file.Flush(flushToDisk: true);
        }

        File.Move(temporaryFile, path, overwrite: true);
    }
}
