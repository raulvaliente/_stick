using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace _stick;

internal sealed class NoteCollection
{
    public required string Executable { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsBuiltIn { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomName { get; set; }
    public bool Automatic { get; set; } = true;
    public List<Note> Notes { get; init; } = [];
    public List<Note> TrashedNotes { get; init; } = [];

    [JsonIgnore]
    public string Name => IsBuiltIn
        ? "_stick"
        : string.IsNullOrWhiteSpace(CustomName) ? Path.GetFileNameWithoutExtension(Executable) : CustomName;
}

internal sealed class Note
{
    public const double DefaultSize = 240;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsWelcome { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsMarkdownGuide { get; init; }
    public string Text { get; set; } = "";
    public string Color { get; set; } = "#FFF0AB";
    public double Width { get; set; } = DefaultSize;
    public double Height { get; set; } = DefaultSize;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Left { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Top { get; set; }
}
