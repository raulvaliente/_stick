using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace _stick;

// The TextBox owns the Markdown source, caret and undo history.
internal sealed class MarkdownPreview : FrameworkElement
{
    private static readonly FontFamily CodeFont = new("Consolas");
    private TextBox? editor;
    private string? source;
    private List<MarkdownLine> lines = [];
    private bool renderPending;

    public void Attach(TextBox textBox)
    {
        editor = textBox;
        textBox.Loaded += (_, _) => RequestRender();
        textBox.TextChanged += (_, _) => RequestRender();
        textBox.SelectionChanged += (_, _) => RequestRender();
        textBox.IsKeyboardFocusWithinChanged += (_, _) => RequestRender();
        textBox.SizeChanged += (_, _) => RequestRender();
        textBox.AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) => RequestRender()));
    }

    private void RequestRender()
    {
        if (renderPending)
            return;

        // TextBox updates its wrapped line map during layout, after TextChanged.
        renderPending = true;
        Dispatcher.InvokeAsync(() =>
        {
            renderPending = false;
            InvalidateVisual();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (editor is null || !editor.IsMeasureValid || !editor.IsArrangeValid
            || editor.SelectionLength != 0 && (editor.IsKeyboardFocusWithin || editor.ContextMenu?.IsOpen == true))
            return;

        if (source != editor.Text)
        {
            source = editor.Text;
            lines = MarkdownLine.Parse(source);
        }

        if (lines.Count == 0 || editor.Template.FindName("PART_ContentHost", editor) is not ScrollViewer scroll
            || scroll.Template.FindName("PART_ScrollContentPresenter", scroll) is not ScrollContentPresenter presenter
            || presenter.ActualWidth <= 0 || presenter.ActualHeight <= 0)
            return;

        var origin = presenter.TranslatePoint(new Point(), this);
        var viewport = new Rect(origin.X, origin.Y, presenter.ActualWidth, presenter.ActualHeight);
        var firstRow = editor.GetFirstVisibleLineIndex();
        var lastRow = Math.Min(editor.GetLastVisibleLineIndex(), editor.LineCount - 1);
        if (firstRow < 0 || lastRow < firstRow)
            return;

        var caret = editor.IsKeyboardFocusWithin ? editor.CaretIndex : -1;
        var lineIndex = 0;
        drawingContext.PushClip(new RectangleGeometry(viewport));
        for (var row = firstRow; row <= lastRow; row++)
        {
            if (row >= editor.LineCount)
                break;

            int start;
            int length;
            try
            {
                start = editor.GetCharacterIndexFromLineIndex(row);
                length = editor.GetLineLength(row);
            }
            catch (ArgumentOutOfRangeException)
            {
                // The TextBox can invalidate its visual line map while formatting a resize.
                break;
            }

            if (start < 0 || start >= source.Length || length <= 0)
                continue;

            while (lineIndex < lines.Count && lines[lineIndex].End < start)
                lineIndex++;
            if (lineIndex == lines.Count)
                break;

            var line = lines[lineIndex];
            if (line.Start > start || (caret >= line.Start && caret <= line.End))
                continue;

            var bounds = editor.GetRectFromCharacterIndex(start);
            if (bounds.IsEmpty || bounds.Height <= 0)
                continue;

            bounds.Offset(editor.TranslatePoint(new Point(), this) - new Point());
            DrawRow(drawingContext, editor, viewport, bounds, line, start, Math.Min(start + length, line.End));
        }
        drawingContext.Pop();
    }

    private void DrawRow(DrawingContext drawing, TextBox textBox, Rect viewport, Rect bounds,
        MarkdownLine line, int start, int end)
    {
        var band = new Rect(viewport.Left, bounds.Top, viewport.Width, bounds.Height);
        drawing.DrawRectangle(textBox.Background, null, band);
        if (line.Table is { } table)
        {
            DrawTableRow(drawing, textBox, band, line.Block, table, start == line.Start);
            return;
        }

        if (line.Block == MarkdownBlock.Rule)
        {
            drawing.DrawLine(new Pen(Tint(textBox, 105), 1), new Point(band.Left + 3, band.Top + band.Height / 2),
                new Point(band.Right - 3, band.Top + band.Height / 2));
            return;
        }

        var x = Math.Max(bounds.Left, viewport.Left);
        if (line.Block == MarkdownBlock.Quote)
        {
            drawing.DrawRectangle(Tint(textBox, 115), null, new Rect(x, band.Top + 1, 2, Math.Max(0, band.Height - 2)));
            x += 8;
        }
        else if (line.Block is MarkdownBlock.Code or MarkdownBlock.Fence)
        {
            drawing.DrawRectangle(Tint(textBox, 26), null, band);
            x += 4;
        }

        var text = new StringBuilder();
        var styles = new List<(int Start, int Length, MarkdownStyle Style)>();
        if (line.Block == MarkdownBlock.Fence && start == line.Start)
        {
            if (line.Label is { } label)
                text.Append(label.Length == 0 ? "CÓDIGO" : $"CÓDIGO · {label}");
            styles.Add((0, text.Length, MarkdownStyle.Bold | MarkdownStyle.Code));
        }
        else
        {
            foreach (var part in line.Parts)
            {
                var from = Math.Max(start, part.Start);
                var to = Math.Min(end, part.Start + part.Text.Length);
                if (from >= to)
                    continue;

                var offset = text.Length;
                if (part.IsBullet)
                {
                    if (part.Start >= start)
                        text.Append(part.Text);
                }
                else
                {
                    text.Append(part.Text, from - part.Start, to - from);
                }

                if (part.Style != MarkdownStyle.Plain && text.Length > offset)
                    styles.Add((offset, text.Length - offset, part.Style));
            }
        }

        if (text.Length == 0 || x >= viewport.Right)
            return;

        var heading = line.HeadingLevel > 0;
        var size = heading ? textBox.FontSize + (line.HeadingLevel <= 2 ? 2 : 1) : textBox.FontSize;
        var formatted = MakeText(textBox, text.ToString(), size, heading);
        PaintText(drawing, textBox, formatted, styles, new Rect(x, band.Top, viewport.Right - x, band.Height));
    }

    private void DrawTableRow(DrawingContext drawing, TextBox editor, Rect band,
        MarkdownBlock block, MarkdownTableRow table, bool firstVisualRow)
    {
        var heading = block == MarkdownBlock.TableHeader;
        drawing.DrawRectangle(Tint(editor, heading ? (byte)37 : (byte)14), null, band);
        var border = new Pen(Tint(editor, 100), 1);
        if (heading && firstVisualRow)
            drawing.DrawLine(border, band.TopLeft, band.TopRight);
        if (block == MarkdownBlock.TableDivider)
            drawing.DrawLine(border, new Point(band.Left, band.Top + 1), new Point(band.Right, band.Top + 1));
        else
            drawing.DrawLine(border, band.BottomLeft, band.BottomRight);

        var columnWidth = band.Width / table.Alignments.Length;
        for (var column = 0; column <= table.Alignments.Length; column++)
        {
            var left = band.Left + column * columnWidth;
            drawing.DrawLine(border, new Point(left, band.Top), new Point(left, band.Bottom));
            if (!firstVisualRow || column >= table.Cells.Length || column >= table.Alignments.Length
                || columnWidth <= 8 || string.IsNullOrEmpty(table.Cells[column]))
                continue;

            var text = new StringBuilder();
            var styles = new List<(int Start, int Length, MarkdownStyle Style)>();
            foreach (var part in MarkdownLine.InlineParts(table.Cells[column]))
            {
                styles.Add((text.Length, part.Text.Length, part.Style));
                text.Append(part.Text);
            }

            var formatted = MakeText(editor, text.ToString(), editor.FontSize, heading);
            var width = columnWidth - 8;
            var alignmentOffset = Math.Max(0, width - formatted.Width) * (table.Alignments[column] switch
            {
                MarkdownAlignment.Right => 1,
                MarkdownAlignment.Center => .5,
                _ => 0
            });
            PaintText(drawing, editor, formatted, styles,
                new Rect(left + 4 + alignmentOffset, band.Top, width - alignmentOffset, band.Height));
        }
    }

    private FormattedText MakeText(TextBox editor, string text, double size, bool bold) =>
        new(text, CultureInfo.CurrentCulture, editor.FlowDirection,
            new Typeface(editor.FontFamily, editor.FontStyle, bold ? FontWeights.Bold : editor.FontWeight, editor.FontStretch),
            size, editor.Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static void PaintText(DrawingContext drawing, TextBox editor, FormattedText formatted,
        List<(int Start, int Length, MarkdownStyle Style)> styles, Rect bounds)
    {
        foreach (var (start, length, style) in styles)
        {
            if (style.HasFlag(MarkdownStyle.Bold))
                formatted.SetFontWeight(FontWeights.Bold, start, length);
            if (style.HasFlag(MarkdownStyle.Italic))
                formatted.SetFontStyle(FontStyles.Italic, start, length);
            if (style.HasFlag(MarkdownStyle.Code))
                formatted.SetFontFamily(CodeFont, start, length);
            if (style.HasFlag(MarkdownStyle.Strike))
                formatted.SetTextDecorations(TextDecorations.Strikethrough, start, length);
            if (style.HasFlag(MarkdownStyle.Link))
                formatted.SetTextDecorations(TextDecorations.Underline, start, length);
        }

        var scale = Math.Min(1, bounds.Width / Math.Max(1, formatted.Width));
        var origin = new Point(bounds.Left, bounds.Top + (bounds.Height - formatted.Height) / 2);
        drawing.PushClip(new RectangleGeometry(bounds));
        drawing.PushTransform(new ScaleTransform(scale, 1, origin.X, origin.Y));
        foreach (var (start, length, style) in styles)
        {
            if (style.HasFlag(MarkdownStyle.Highlight))
                drawing.DrawGeometry(Tint(editor, 53), null, formatted.BuildHighlightGeometry(origin, start, length));
        }
        drawing.DrawText(formatted, origin);
        drawing.Pop();
        drawing.Pop();
    }

    private static Brush Tint(TextBox editor, byte opacity)
    {
        var color = editor.Foreground is SolidColorBrush brush ? brush.Color : Colors.Black;
        return new SolidColorBrush(Color.FromArgb(opacity, color.R, color.G, color.B));
    }
}
