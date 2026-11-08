using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace _stick;

internal sealed class LocalStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

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
                collection is null || string.IsNullOrWhiteSpace(collection.Executable)
                || collection.Notes is null || collection.Notes.Any(note => note is null)))
            throw new JsonException("El archivo de notas no tiene el formato esperado.");

        return collections;
    }

    public void Save(List<NoteCollection> collections)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temporaryFile = filePath + ".tmp";
        using (var file = File.Create(temporaryFile))
        {
            JsonSerializer.Serialize(file, collections, Options);
            file.Flush(flushToDisk: true);
        }

        File.Move(temporaryFile, filePath, overwrite: true);
    }
}
