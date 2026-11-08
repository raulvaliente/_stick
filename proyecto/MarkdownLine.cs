using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace _stick;

[Flags]
internal enum MarkdownStyle
{
    Plain = 0,
    Bold = 1,
    Italic = 2,
    Code = 4
}

internal sealed record MarkdownPart(int Start, string Text, MarkdownStyle Style, bool IsBullet = false);

internal sealed record MarkdownLine(int Start, int End, int HeadingLevel, List<MarkdownPart> Parts)
{
    private static readonly Regex Heading = new(@"^ {0,3}(?<level>#{1,6})[ \t]+(?=\S)");
    private static readonly Regex Bullet = new(@"^[ \t]*(?<marker>[-+*])[ \t]+(?=\S)");
    private static readonly Regex Ordered = new(@"^[ \t]*[0-9]{1,9}[.)][ \t]+(?=\S)");
    private static readonly Regex Fence = new(@"^ {0,3}(?<marker>\x60{3,}|~{3,})(?<rest>.*)$");
    private static readonly Regex Inline = new("""
        \\[\\*_`#~+\-]
        | (?<!`)(?<ticks>`+)(?<code>[^`]+?)\k<ticks>(?!`)
        | (?<!\*)\*{3}(?<both>[^\s*](?:[^*]*?[^\s*])?)\*{3}(?!\*)
        | (?<!\*)\*\*(?<bold>[^\s*](?:[^*]*?[^\s*])?)\*\*(?!\*)
        | (?<![\p{L}\p{N}_])__(?<bold>[^\s_](?:[^_]*?[^\s_])?)__(?![\p{L}\p{N}_])
        | (?<!\*)\*(?<italic>[^\s*](?:[^*]*?[^\s*])?)\*(?!\*)
        | (?<![\p{L}\p{N}_])_(?<italic>[^\s_](?:[^_]*?[^\s_])?)_(?![\p{L}\p{N}_])
        """, RegexOptions.IgnorePatternWhitespace);

    public static List<MarkdownLine> Parse(string source)
    {
        var lines = new List<MarkdownLine>();
        char fenceMarker = '\0';
        var fenceLength = 0;

        for (var start = 0; start < source.Length;)
        {
            var end = start;
            while (end < source.Length && source[end] is not ('\r' or '\n'))
                end++;
            var line = source[start..end];
            var fence = Fence.Match(line);

            if (fence.Success && fenceLength == 0)
            {
                var marker = fence.Groups["marker"].Value;
                fenceMarker = marker[0];
                fenceLength = marker.Length;
            }
            else if (fenceLength == 0)
            {
                var formatted = ParseLine(line, start);
                if (formatted is not null)
                    lines.Add(formatted);
            }
            else if (fence.Success && fence.Groups["marker"].Value[0] == fenceMarker
                && fence.Groups["marker"].Length >= fenceLength && string.IsNullOrWhiteSpace(fence.Groups["rest"].Value))
            {
                fenceLength = 0;
            }

            start = end + 1;
            if (end < source.Length - 1 && source[end] == '\r' && source[end + 1] == '\n')
                start++;
        }

        return lines;
    }

    private static MarkdownLine? ParseLine(string line, int start)
    {
        var heading = Heading.Match(line);
        var headingLevel = heading.Groups["level"].Length;
        var bullet = Bullet.Match(line);
        var ordered = Ordered.Match(line);
        var parts = new List<MarkdownPart>();
        var position = heading.Length;
        if (bullet.Success)
        {
            var marker = bullet.Groups["marker"].Index;
            AddPlain(parts, line, start, 0, marker);
            parts.Add(new MarkdownPart(start + marker, "• ", MarkdownStyle.Plain, IsBullet: true));
            position = bullet.Length;
        }
        else if (ordered.Success)
        {
            parts.Add(new MarkdownPart(start, ordered.Value, MarkdownStyle.Bold));
            position = ordered.Length;
        }

        var formatted = position > 0;
        foreach (Match match in Inline.Matches(line, position))
        {
            if (match.Value[0] == '\\')
                continue;

            AddPlain(parts, line, start, position, match.Index);
            if (match.Groups["code"].Success)
                AddStyled(parts, match.Groups["code"], start, MarkdownStyle.Code);
            else if (match.Groups["both"].Success)
                AddStyled(parts, match.Groups["both"], start, MarkdownStyle.Bold | MarkdownStyle.Italic);
            else if (match.Groups["bold"].Success)
                AddStyled(parts, match.Groups["bold"], start, MarkdownStyle.Bold);
            else
                AddStyled(parts, match.Groups["italic"], start, MarkdownStyle.Italic);

            position = match.Index + match.Length;
            formatted = true;
        }

        if (!formatted)
            return null;

        AddPlain(parts, line, start, position, line.Length);
        return new MarkdownLine(start, start + line.Length, headingLevel, parts);
    }

    private static void AddPlain(List<MarkdownPart> parts, string line, int start, int from, int to)
    {
        if (from < to)
            parts.Add(new MarkdownPart(start + from, line[from..to], MarkdownStyle.Plain));
    }

    private static void AddStyled(List<MarkdownPart> parts, Group group, int start, MarkdownStyle style) =>
        parts.Add(new MarkdownPart(start + group.Index, group.Value, style));
}
