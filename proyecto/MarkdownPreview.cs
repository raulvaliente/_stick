using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace _stick;

// Paints the preview over inactive lines. The TextBox still owns the source, caret, and undo history.
internal sealed class MarkdownPreview : FrameworkElement
{
    private static readonly FontFamily CodeFont = new("Consolas");
    private TextBox? editor;
    private string? source;
    private List<MarkdownLine> lines = [];

    public void Attach(TextBox textBox)
    {
        editor = textBox;
        textBox.Loaded += (_, _) => InvalidateVisual();
        textBox.TextChanged += (_, _) => InvalidateVisual();
        textBox.SelectionChanged += (_, _) => InvalidateVisual();
        textBox.IsKeyboardFocusWithinChanged += (_, _) => InvalidateVisual();
        textBox.SizeChanged += (_, _) => InvalidateVisual();
        textBox.AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) => InvalidateVisual()));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (editor is null || !editor.IsMeasureValid || !editor.IsArrangeValid
            || (editor.IsKeyboardFocusWithin && editor.SelectionLength != 0))
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
        var lastRow = editor.GetLastVisibleLineIndex();
        if (firstRow < 0 || lastRow < firstRow)
            return;

        var caret = editor.IsKeyboardFocusWithin ? editor.CaretIndex : -1;
        var lineIndex = 0;
        drawingContext.PushClip(new RectangleGeometry(viewport));
        for (var row = firstRow; row <= lastRow; row++)
        {
            var start = editor.GetCharacterIndexFromLineIndex(row);
            var length = editor.GetLineLength(row);
            if (start < 0 || length <= 0)
                continue;

            while (lineIndex < lines.Count && lines[lineIndex].End <= start)
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
        var text = new StringBuilder();
        var styles = new List<(int Start, int Length, MarkdownStyle Style)>();
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

            if (part.Style != MarkdownStyle.Plain)
                styles.Add((offset, text.Length - offset, part.Style));
        }

        drawing.DrawRectangle(textBox.Background, null,
            new Rect(viewport.Left, bounds.Top, viewport.Width, bounds.Height));
        if (text.Length == 0)
            return;

        var heading = line.HeadingLevel > 0;
        var size = heading ? textBox.FontSize + (line.HeadingLevel <= 2 ? 2 : 1) : textBox.FontSize;
        var typeface = new Typeface(textBox.FontFamily, textBox.FontStyle,
            heading ? FontWeights.Bold : textBox.FontWeight, textBox.FontStretch);
        var formatted = new FormattedText(text.ToString(), CultureInfo.CurrentCulture, textBox.FlowDirection,
            typeface, size, textBox.Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        foreach (var span in styles)
        {
            if (span.Style.HasFlag(MarkdownStyle.Bold))
                formatted.SetFontWeight(FontWeights.Bold, span.Start, span.Length);
            if (span.Style.HasFlag(MarkdownStyle.Italic))
                formatted.SetFontStyle(FontStyles.Italic, span.Start, span.Length);
            if (span.Style.HasFlag(MarkdownStyle.Code))
                formatted.SetFontFamily(CodeFont, span.Start, span.Length);
        }

        var x = Math.Max(bounds.Left, viewport.Left);
        var width = Math.Max(0, viewport.Right - x);
        if (width == 0)
            return;

        // Keep the source's wrap points so changing focus never moves the following lines.
        var scale = Math.Min(1, width / Math.Max(1, formatted.Width));
        var y = bounds.Top + (bounds.Height - formatted.Height) / 2;
        drawing.PushClip(new RectangleGeometry(new Rect(x, bounds.Top, width, bounds.Height)));
        drawing.PushTransform(new ScaleTransform(scale, 1, x, y));
        drawing.DrawText(formatted, new Point(x, y));
        drawing.Pop();
        drawing.Pop();
    }
}
