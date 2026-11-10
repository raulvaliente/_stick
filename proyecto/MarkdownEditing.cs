using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace _stick;

internal sealed class MarkdownEditing
{
    private sealed record Snippet(string Title, string Text, int Cursor, int Selection = 0, bool Block = false);

    private static readonly (string Title, string Id)[] Languages =
        [("C#", "csharp"), ("Python", "python"), ("JavaScript", "javascript"), ("PowerShell", "powershell"),
            ("SQL", "sql"), ("JSON", "json"), ("Bash", "bash")];
    private static readonly Regex CodePrefix = new(@"^\x60{3}(?<language>[\w#+-]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex BacktickRuns = new(@"\x60+");
    private static readonly Regex TaskPrefix = new(@"^[-+*]\s+\[$");

    private readonly TextBox editor;
    private readonly ListBox suggestions;
    private readonly Popup popup;
    private DispatcherOperation? refresh;
    private int replaceStart;
    private int replaceEnd;
    private bool editing;

    public MarkdownEditing(TextBox editor)
    {
        this.editor = editor;
        suggestions = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(33, 30, 43)),
            Foreground = Brushes.WhiteSmoke,
            FontFamily = editor.FontFamily,
            FontSize = 13,
            DisplayMemberPath = nameof(Snippet.Title),
            MaxHeight = 164,
            Focusable = false,
            ItemContainerStyle = new Style(typeof(ListBoxItem))
            {
                Setters = { new Setter(UIElement.FocusableProperty, false) }
            }
        };
        popup = new Popup
        {
            PlacementTarget = editor,
            Placement = PlacementMode.Relative,
            StaysOpen = false,
            Focusable = false,
            Child = suggestions
        };
        suggestions.PreviewMouseLeftButtonUp += ChooseWithMouse;
        editor.ContextMenu = BuildContextMenu();
        editor.PreviewKeyDown += EditorKeyDown;
        editor.TextChanged += (_, _) => ScheduleSuggestions();
        editor.SelectionChanged += (_, _) =>
        {
            if (editor.SelectionLength != 0 || editor.CaretIndex != replaceEnd)
                popup.IsOpen = false;
        };
        editor.LostKeyboardFocus += (_, _) => CloseSuggestions();
        editor.Unloaded += (_, _) => CloseSuggestions();
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu { FontFamily = editor.FontFamily };
        menu.Items.Add(Command("Cortar", ApplicationCommands.Cut));
        menu.Items.Add(Command("Copiar", ApplicationCommands.Copy));
        menu.Items.Add(Command("Pegar", ApplicationCommands.Paste));
        menu.Items.Add(Command("Seleccionar todo", ApplicationCommands.SelectAll));
        menu.Items.Add(new Separator());

        var format = new MenuItem { Header = "Formato" };
        var inline = new[]
        {
            ActionItem("Negrita", () => WrapInline("**")),
            ActionItem("Cursiva", () => WrapInline("*")),
            ActionItem("Tachado", () => WrapInline("~~")),
            ActionItem("Resaltado", () => WrapInline("==")),
            ActionItem("Código en línea", WrapInlineCode)
        };
        foreach (var item in inline)
            format.Items.Add(item);
        format.Items.Add(new Separator());
        format.Items.Add(ActionItem("Encabezado 1", () => PrefixLines("# ")));
        format.Items.Add(ActionItem("Encabezado 2", () => PrefixLines("## ")));
        format.Items.Add(ActionItem("Encabezado 3", () => PrefixLines("### ")));
        format.Items.Add(ActionItem("Cita", () => PrefixLines("> ")));
        format.Items.Add(ActionItem("Lista con viñetas", () => PrefixLines("- ")));
        format.Items.Add(ActionItem("Lista de tareas", () => PrefixLines("- [ ] ")));
        menu.Items.Add(format);

        var insert = new MenuItem { Header = "Insertar" };
        insert.Items.Add(ActionItem("Tabla", () => Insert(Table())));
        var link = ActionItem("Enlace", InsertLink);
        insert.Items.Add(link);
        insert.Items.Add(ActionItem("Bloque de código", () => InsertCode("")));
        var byLanguage = new MenuItem { Header = "Código con lenguaje" };
        foreach (var (title, id) in Languages)
            byLanguage.Items.Add(ActionItem(title, () => InsertCode(id)));
        insert.Items.Add(byLanguage);
        insert.Items.Add(ActionItem("Separador", () => Insert(new("Separador", "---", 3, Block: true))));
        menu.Items.Add(insert);

        menu.Opened += (_, _) =>
        {
            CloseSuggestions();
            var multiline = editor.SelectedText.IndexOfAny(['\r', '\n']) >= 0;
            foreach (var item in inline)
                item.IsEnabled = !multiline;
            link.IsEnabled = !multiline;
        };
        return menu;
    }

    private MenuItem Command(string title, RoutedUICommand command) =>
        new() { Header = title, Command = command, CommandTarget = editor };

    private static MenuItem ActionItem(string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action();
        return item;
    }

    private void ScheduleSuggestions()
    {
        if (editing || !editor.IsKeyboardFocusWithin)
            return;

        refresh?.Abort();
        refresh = editor.Dispatcher.InvokeAsync(() => UpdateSuggestions(manual: false), DispatcherPriority.Background);
    }

    private void UpdateSuggestions(bool manual)
    {
        if (!editor.IsKeyboardFocusWithin || editor.SelectionLength != 0 || editor.ContextMenu.IsOpen)
            return;

        var source = editor.Text;
        var caret = editor.CaretIndex;
        var lineStart = LineStart(source, caret);
        var prefix = source[lineStart..caret];
        var indentation = prefix.Length - prefix.TrimStart(' ', '\t').Length;
        var trimmed = prefix[indentation..];
        var start = lineStart + indentation;
        var code = CodePrefix.Match(trimmed);
        List<Snippet> matches;

        if (code.Success)
        {
            var language = code.Groups["language"].Value;
            matches = Languages.Where(item => item.Id.StartsWith(language, StringComparison.OrdinalIgnoreCase)
                    || item.Title.StartsWith(language, StringComparison.OrdinalIgnoreCase))
                .Select(item => Code(item.Title, item.Id)).ToList();
            if (language.Length == 0)
                matches.Insert(0, Code("Bloque de código", ""));
            else if (matches.Count == 0)
                matches.Add(Code($"Código · {language}", language));
        }
        else if (trimmed == "|")
            matches = [Table()];
        else if (TaskPrefix.IsMatch(trimmed))
            matches = [new("Tarea pendiente", "- [ ] ", 6), new("Tarea completada", "- [x] ", 6)];
        else if (trimmed.Length is >= 1 and <= 6 && trimmed.All(character => character == '#'))
            matches = [new($"Encabezado {trimmed.Length}", trimmed + " ", trimmed.Length + 1)];
        else if (trimmed == ">")
            matches = [new("Cita", "> ", 2)];
        else if (trimmed == "-")
            matches = [new("Lista con viñetas", "- ", 2), new("Tarea pendiente", "- [ ] ", 6)];
        else if (trimmed == "---")
            matches = [new("Separador", "---", 3, Block: true)];
        else if (TryInlineSuggestion(trimmed) is { } inline)
        {
            start = caret - inline.Prefix.Length;
            matches = [inline.Snippet];
        }
        else if (manual)
        {
            start = caret;
            matches = [Table(), Code("Bloque de código", ""), Link(), new("Tarea pendiente", "- [ ] ", 6, Block: true)];
        }
        else
        {
            popup.IsOpen = false;
            return;
        }

        if (MarkdownLine.Parse(source).Any(line => line.Start == lineStart
                && (line.Block == MarkdownBlock.Code || line.Block == MarkdownBlock.Fence && line.Label is null)))
        {
            popup.IsOpen = false;
            return;
        }

        var bounds = editor.IsArrangeValid ? editor.GetRectFromCharacterIndex(caret) : Rect.Empty;
        if (bounds.IsEmpty)
            return;

        replaceStart = start;
        replaceEnd = caret;
        suggestions.ItemsSource = matches;
        suggestions.SelectedIndex = 0;
        popup.HorizontalOffset = Math.Max(0, Math.Min(bounds.Left, editor.ActualWidth - 120));
        popup.VerticalOffset = bounds.Bottom + 2;
        popup.IsOpen = true;
    }

    private static (string Prefix, Snippet Snippet)? TryInlineSuggestion(string prefix)
    {
        foreach (var (marker, label) in new[]
        {
            ("**", "Negrita"), ("*", "Cursiva"), ("~~", "Tachado"), ("==", "Resaltado"), ("\u0060", "Código en línea"), ("[", "Enlace")
        })
        {
            if (!prefix.EndsWith(marker, StringComparison.Ordinal))
                continue;

            var index = prefix.Length - marker.Length;
            if (index > 0 && !char.IsWhiteSpace(prefix[index - 1]) && prefix[index - 1] != '(')
                continue;

            return (marker, marker == "[" ? Link() : new(label, marker + marker, marker.Length));
        }

        return null;
    }

    private void EditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            UpdateSuggestions(manual: true);
            return;
        }

        if (!popup.IsOpen)
            return;

        switch (e.Key)
        {
            case Key.Down:
            case Key.Up:
                e.Handled = true;
                var direction = e.Key == Key.Down ? 1 : -1;
                suggestions.SelectedIndex = Math.Clamp(suggestions.SelectedIndex + direction, 0, suggestions.Items.Count - 1);
                suggestions.ScrollIntoView(suggestions.SelectedItem);
                break;
            case Key.Enter:
            case Key.Tab:
                e.Handled = true;
                AcceptSuggestion();
                break;
            case Key.Escape:
                e.Handled = true;
                CloseSuggestions();
                break;
        }
    }

    private void ChooseWithMouse(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(suggestions, source) is not ListBoxItem item)
            return;

        suggestions.SelectedItem = item.Content;
        e.Handled = true;
        AcceptSuggestion();
    }

    private void AcceptSuggestion()
    {
        if (suggestions.SelectedItem is Snippet snippet && editor.CaretIndex == replaceEnd && editor.SelectionLength == 0)
            InsertAt(replaceStart, replaceEnd - replaceStart, snippet);
        CloseSuggestions();
    }

    private void CloseSuggestions()
    {
        refresh?.Abort();
        popup.IsOpen = false;
    }

    private void WrapInlineCode()
    {
        var marker = new string('\u0060', LongestBacktickRun(editor.SelectedText) + 1);
        WrapInline(marker);
    }

    private void WrapInline(string marker)
    {
        var start = editor.SelectionStart;
        var text = editor.SelectedText;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            if (text.Length == 0)
                Replace(start, 0, marker + marker, marker.Length);
            return;
        }

        start += text.Length - text.TrimStart().Length;
        var end = start + trimmed.Length;
        var source = editor.Text;
        if (trimmed.Length > marker.Length * 2 && trimmed.StartsWith(marker) && trimmed.EndsWith(marker))
        {
            var unwrapped = trimmed[marker.Length..^marker.Length];
            Replace(start, trimmed.Length, unwrapped, 0, unwrapped.Length);
        }
        else if (start >= marker.Length && end + marker.Length <= source.Length
            && source.AsSpan(start - marker.Length, marker.Length).SequenceEqual(marker)
            && source.AsSpan(end, marker.Length).SequenceEqual(marker))
        {
            Replace(start - marker.Length, trimmed.Length + marker.Length * 2, trimmed, 0, trimmed.Length);
        }
        else
        {
            var needsSpace = marker[0] == '\u0060' && (trimmed.StartsWith('\u0060') || trimmed.EndsWith('\u0060'));
            var padding = needsSpace ? " " : "";
            Replace(start, trimmed.Length, marker + padding + trimmed + padding + marker,
                marker.Length + padding.Length, trimmed.Length);
        }
    }

    private void PrefixLines(string prefix)
    {
        var source = editor.Text;
        var start = LineStart(source, editor.SelectionStart);
        var end = editor.SelectionStart + editor.SelectionLength;
        if (editor.SelectionLength > 0 && end > start && source[end - 1] is '\r' or '\n')
            while (end > start && source[end - 1] is '\r' or '\n')
                end--;
        else
            while (end < source.Length && source[end] is not ('\r' or '\n'))
                end++;

        var lines = Regex.Split(source[start..end], @"\r\n|\r|\n");
        var remove = lines.All(line => line.StartsWith(prefix, StringComparison.Ordinal));
        var result = string.Join(NewLine, lines.Select(line => remove ? line[prefix.Length..] : prefix + line));
        if (editor.SelectionLength == 0)
        {
            var originalOffset = editor.CaretIndex - start;
            var cursor = remove ? Math.Max(0, originalOffset - prefix.Length) : originalOffset + prefix.Length;
            Replace(start, end - start, result, cursor);
        }
        else
            Replace(start, end - start, result, 0, result.Length);
    }

    private void InsertLink()
    {
        var label = editor.SelectionLength == 0 ? "texto" : editor.SelectedText;
        const string url = "https://";
        Insert(new("Enlace", $"[{label}]({url})", label.Length + 3, url.Length));
    }

    private void InsertCode(string language)
    {
        var selected = editor.SelectedText;
        var marker = new string('\u0060', Math.Max(3, LongestBacktickRun(selected) + 1));
        var header = marker + language + NewLine;
        Insert(new("Bloque de código", header + selected + NewLine + marker, header.Length, selected.Length, Block: true));
    }

    private Snippet Code(string title, string language)
    {
        var opening = "\u0060\u0060\u0060" + language + NewLine;
        return new(title, opening + NewLine + "\u0060\u0060\u0060", opening.Length, Block: true);
    }

    private Snippet Table() => new("Tabla de dos columnas",
        $"| Columna 1 | Columna 2 |{NewLine}| --- | --- |{NewLine}| Dato | Dato |", 2, "Columna 1".Length, Block: true);

    private static Snippet Link() => new("Enlace", "[texto](https://)", 1, 5);

    private string NewLine => editor.Text.Contains('\n') && !editor.Text.Contains("\r\n", StringComparison.Ordinal) ? "\n" : "\r\n";

    private static int LongestBacktickRun(string text) =>
        BacktickRuns.Matches(text).Select(match => match.Length).DefaultIfEmpty().Max();

    private static int LineStart(string source, int position)
    {
        while (position > 0 && source[position - 1] is not ('\r' or '\n'))
            position--;
        return position;
    }

    private void Insert(Snippet snippet) => InsertAt(editor.SelectionStart, editor.SelectionLength, snippet);

    private void InsertAt(int start, int length, Snippet snippet)
    {
        var source = editor.Text;
        var before = snippet.Block && start > 0 && source[start - 1] is not ('\r' or '\n') ? NewLine : "";
        var end = start + length;
        var after = snippet.Block && end < source.Length && source[end] is not ('\r' or '\n') ? NewLine : "";
        Replace(start, length, before + snippet.Text + after, before.Length + snippet.Cursor, snippet.Selection);
    }

    private void Replace(int start, int length, string text, int cursor, int selection = 0)
    {
        CloseSuggestions();
        editor.Focus();
        editing = true;
        editor.BeginChange();
        try
        {
            editor.Select(start, length);
            editor.SelectedText = text;
            editor.Select(start + cursor, selection);
        }
        finally
        {
            editor.EndChange();
            editing = false;
        }
    }
}
