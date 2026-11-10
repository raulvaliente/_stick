using System;
using System.ComponentModel;
using System.Windows.Media;

namespace _stick;

internal sealed class NoteViewModel(Note note, Action changed) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsWelcome => note.IsWelcome;
    public bool IsMarkdownGuide => note.IsMarkdownGuide;
    public bool IsTutorial => IsWelcome || IsMarkdownGuide;
    public double Width => note.Width;
    public double Height => note.Height;

    public string Text
    {
        get => note.Text;
        set
        {
            if (note.Text == value)
                return;

            note.Text = value;
            PropertyChanged?.Invoke(this, new(nameof(Text)));
            changed();
        }
    }

    public Color Color => ReadColor();
    public Brush Background => new SolidColorBrush(Color);
    public Brush Foreground => ContrastingForeground(Color);

    internal static Brush ContrastingForeground(Color color)
    {
        var luminance = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        return luminance > .179 ? Brushes.Black : Brushes.White;
    }

    public void SetColor(Color color)
    {
        note.Color = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        PropertyChanged?.Invoke(this, new(nameof(Background)));
        PropertyChanged?.Invoke(this, new(nameof(Foreground)));
        changed();
    }

    public void SetSize(double width, double height)
    {
        if (note.Width == width && note.Height == height)
            return;

        note.Width = width;
        note.Height = height;
        changed();
    }

    public void SetPosition(int left, int top)
    {
        if (note.Left == left && note.Top == top)
            return;

        note.Left = left;
        note.Top = top;
        changed();
    }

    private Color ReadColor()
    {
        try
        {
            if (ColorConverter.ConvertFromString(note.Color ?? "") is Color color)
                return color;
        }
        catch (FormatException)
        {
            // Keep the note readable if its saved color is invalid.
        }

        return Colors.LemonChiffon;
    }

    private static double Linear(byte channel)
    {
        var value = channel / 255d;
        return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    }
}
