using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace _stick;

[Flags]
internal enum MarkdownStyle
{
    Plain = 0, Bold = 1, Italic = 2, Code = 4, Strike = 8, Highlight = 16, Link = 32
}

internal enum MarkdownBlock
{
    Text, Quote, Code, Fence, Rule, TableHeader, TableDivider, TableRow
}

internal enum MarkdownAlignment { Left, Center, Right }

internal sealed record MarkdownTableRow(string[] Cells, MarkdownAlignment[] Alignments);

internal sealed record MarkdownPart(int Start, string Text, MarkdownStyle Style, bool IsBullet = false);

internal sealed record MarkdownLine(int Start, int End, int HeadingLevel, List<MarkdownPart> Parts,
    MarkdownBlock Block = MarkdownBlock.Text, string? Label = null, MarkdownTableRow? Table = null)
{
    private static readonly Regex Heading = new(@"^ {0,3}(?<level>#{1,6})[ \t]+(?=\S)");
    private static readonly Regex Bullet = new(@"^[ \t]*(?<marker>[-+*])[ \t]+(?=\S)");
    private static readonly Regex Ordered = new(@"^[ \t]*[0-9]{1,9}[.)][ \t]+(?=\S)");
    private static readonly Regex Task = new(@"^[ \t]*(?:[-+*]|[0-9]{1,9}[.)])[ \t]+\[(?<check>[ xX])\](?:[ \t]+|$)");
    private static readonly Regex Quote = new(@"^ {0,3}>[ \t]?");
    private static readonly Regex Rule = new(@"^ {0,3}(?:(?:-[ \t]*){3,}|(?:\*[ \t]*){3,}|(?:_[ \t]*){3,})$");
    private static readonly Regex Fence = new(@"^ {0,3}(?<marker>\x60{3,}|~{3,})(?<rest>.*)$");
    private static readonly Regex Divider = new(@"^:?-+:?$");
    private static readonly Regex Inline = new("""
        \\[\\*_\x60#~+=\[\]\-]
        | (?<!\x60)(?<ticks>\x60+)(?!\x60)(?<code>.+?)(?<!\x60)\k<ticks>(?!\x60)
        | \[(?<link>[^\[\]]+)\]\([^\s)]+\)
        | ~~(?<strike>[^\s~](?:[^~]*?[^\s~])?)~~
        | ==(?<highlight>[^\s=](?:[^=]*?[^\s=])?)==
        | (?<!\*)\*{3}(?<both>[^\s*](?:[^*]*?[^\s*])?)\*{3}(?!\*)
        | (?<!\*)\*\*(?<bold>[^\s*](?:[^*]*?[^\s*])?)\*\*(?!\*)
        | (?<![\p{L}\p{N}_])__(?<bold>[^\s_](?:[^_]*?[^\s_])?)__(?![\p{L}\p{N}_])
        | (?<!\*)\*(?<italic>[^\s*](?:[^*]*?[^\s*])?)\*(?!\*)
        | (?<![\p{L}\p{N}_])_(?<italic>[^\s_](?:[^_]*?[^\s_])?)_(?![\p{L}\p{N}_])
        """, RegexOptions.IgnorePatternWhitespace);

    public static List<MarkdownLine> Parse(string source)
    {
        var lines = new List<MarkdownLine>();
        var rows = ReadLines(source);
        string? fenceMarker = null;

        for (var index = 0; index < rows.Count; index++)
        {
            var (start, text) = rows[index];
            var end = start + text.Length;
            var fence = Fence.Match(text);
            var marker = fence.Groups["marker"].Value;
            var info = fence.Groups["rest"].Value.Trim();

            if (fenceMarker is not null)
            {
                if (marker.Length >= fenceMarker.Length && marker[0] == fenceMarker[0] && info.Length == 0)
                {
                    lines.Add(new(start, end, 0, [], MarkdownBlock.Fence));
                    fenceMarker = null;
                }
                else
                {
                    lines.Add(new(start, end, 0, [new(start, text, MarkdownStyle.Code)], MarkdownBlock.Code));
                }
                continue;
            }

            if (fence.Success && (marker[0] == '~' || !info.Contains('\u0060')))
            {
                fenceMarker = marker;
                lines.Add(new(start, end, 0, [], MarkdownBlock.Fence, info));
                continue;
            }

            if (index + 1 < rows.Count && ReadCells(text) is { } header
                && ReadCells(rows[index + 1].Text) is { } divider && header.Length == divider.Length
                && divider.All(cell => Divider.IsMatch(cell)))
            {
                var alignments = divider.Select(GetAlignment).ToArray();
                lines.Add(new(start, end, 0, [], MarkdownBlock.TableHeader, Table: new(header, alignments)));
                var (dividerStart, dividerText) = rows[++index];
                lines.Add(new(dividerStart, dividerStart + dividerText.Length, 0, [],
                    MarkdownBlock.TableDivider, Table: new([], alignments)));
                while (index + 1 < rows.Count && !Fence.IsMatch(rows[index + 1].Text)
                    && ReadCells(rows[index + 1].Text) is { } cells)
                {
                    var (rowStart, rowText) = rows[++index];
                    lines.Add(new(rowStart, rowStart + rowText.Length, 0, [],
                        MarkdownBlock.TableRow, Table: new(cells, alignments)));
                }
                continue;
            }

            if (ParseLine(text, start) is { } line)
                lines.Add(line);
        }

        return lines;
    }

    private static List<(int Start, string Text)> ReadLines(string source)
    {
        var rows = new List<(int Start, string Text)>();
        var start = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] is not ('\r' or '\n'))
                continue;

            rows.Add((start, source[start..index]));
            if (source[index] == '\r' && index + 1 < source.Length && source[index + 1] == '\n')
                index++;
            start = index + 1;
        }

        rows.Add((start, source[start..]));
        return rows;
    }

    private static string[]? ReadCells(string line)
    {
        var text = line.Trim();
        if (!text.Contains('|'))
            return null;

        var cells = new List<string>();
        var cell = new StringBuilder();
        var separators = 0;
        var endsWithSeparator = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\\' && index + 1 < text.Length && text[index + 1] is '|' or '\\')
            {
                cell.Append(text[++index]);
                continue;
            }

            if (character != '|')
            {
                cell.Append(character);
                continue;
            }

            separators++;
            if (index != 0)
                cells.Add(cell.ToString().Trim());
            cell.Clear();
            endsWithSeparator = index == text.Length - 1;
        }

        if (!endsWithSeparator)
            cells.Add(cell.ToString().Trim());

        return separators > 0 && cells.Count > 0 ? cells.ToArray() : null;
    }

    private static MarkdownAlignment GetAlignment(string divider) => divider.EndsWith(':')
        ? divider.StartsWith(':') ? MarkdownAlignment.Center : MarkdownAlignment.Right
        : MarkdownAlignment.Left;

    private static MarkdownLine? ParseLine(string text, int start)
    {
        var end = start + text.Length;
        if (Rule.IsMatch(text))
            return new(start, end, 0, [], MarkdownBlock.Rule);

        var quote = Quote.Match(text);
        var heading = Heading.Match(text);
        var bullet = Bullet.Match(text);
        var ordered = Ordered.Match(text);
        var task = Task.Match(text);
        var parts = new List<MarkdownPart>();
        var position = quote.Length + heading.Length;
        if (task.Success)
        {
            parts.Add(new(start, task.Groups["check"].Value == " " ? "☐ " : "☑ ", MarkdownStyle.Plain, IsBullet: true));
            position = task.Length;
        }
        else if (bullet.Success)
        {
            var marker = bullet.Groups["marker"].Index;
            AddPlain(parts, text, start, 0, marker);
            parts.Add(new(start + marker, "• ", MarkdownStyle.Plain, IsBullet: true));
            position = bullet.Length;
        }
        else if (ordered.Success)
        {
            parts.Add(new(start, ordered.Value, MarkdownStyle.Bold));
            position = ordered.Length;
        }

        var formatted = ParseInline(text, start, position, parts);
        return formatted || position > 0
            ? new(start, end, heading.Groups["level"].Length, parts, quote.Success ? MarkdownBlock.Quote : MarkdownBlock.Text)
            : null;
    }

    public static List<MarkdownPart> InlineParts(string text)
    {
        var parts = new List<MarkdownPart>();
        ParseInline(text, 0, 0, parts);
        return parts;
    }

    private static bool ParseInline(string text, int start, int position, List<MarkdownPart> parts)
    {
        var formatted = false;
        foreach (Match match in Inline.Matches(text, position))
        {
            if (match.Value[0] == '\\')
                continue;

            AddPlain(parts, text, start, position, match.Index);
            var (group, style) = match switch
            {
                _ when match.Groups["code"].Success => ("code", MarkdownStyle.Code),
                _ when match.Groups["both"].Success => ("both", MarkdownStyle.Bold | MarkdownStyle.Italic),
                _ when match.Groups["bold"].Success => ("bold", MarkdownStyle.Bold),
                _ when match.Groups["strike"].Success => ("strike", MarkdownStyle.Strike),
                _ when match.Groups["highlight"].Success => ("highlight", MarkdownStyle.Highlight),
                _ when match.Groups["link"].Success => ("link", MarkdownStyle.Link),
                _ => ("italic", MarkdownStyle.Italic)
            };
            var value = match.Groups[group];
            parts.Add(new(start + value.Index, value.Value, style));
            position = match.Index + match.Length;
            formatted = true;
        }

        AddPlain(parts, text, start, position, text.Length);
        return formatted;
    }

    private static void AddPlain(List<MarkdownPart> parts, string text, int start, int from, int to)
    {
        if (from < to)
            parts.Add(new(start + from, text[from..to], MarkdownStyle.Plain));
    }
}
