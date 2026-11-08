using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace _stick;

internal sealed class NoteCollection
{
    public required string Executable { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomName { get; set; }
    public bool Automatic { get; set; } = true;
    public List<Note> Notes { get; init; } = [];

    [JsonIgnore]
    public string Name => string.IsNullOrWhiteSpace(CustomName)
        ? Path.GetFileNameWithoutExtension(Executable)
        : CustomName;
}

internal sealed class Note
{
    public const double DefaultSize = 240;

    public string Text { get; set; } = "";
    public string Color { get; set; } = "#FFF0AB";
    public double Width { get; set; } = DefaultSize;
    public double Height { get; set; } = DefaultSize;
}
