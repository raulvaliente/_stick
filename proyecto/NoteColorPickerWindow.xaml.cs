using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace _stick;

public partial class NoteColorPickerWindow : Window
{
    private static readonly string[] DefaultColors =
        ["#FFF0AB", "#FFE275", "#FFB8AA", "#F6BCDB", "#D7C2F2", "#BFD9FF", "#A9E6D5", "#C9E8A3", "#F5F0E7", "#383146"];

    private readonly ObservableCollection<string> favorites;
    private readonly Func<string, bool> saveColor;
    private bool updating = true;

    internal NoteColorPickerWindow(Color current, IEnumerable<string> palette, Func<string, bool> saveColor)
    {
        this.saveColor = saveColor;
        favorites = new(palette);
        InitializeComponent();
        Presets.ItemsSource = DefaultColors;
        Favorites.ItemsSource = favorites;
        EmptyPalette.Visibility = favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectColor(current);
    }

    internal Color SelectedColor { get; private set; }

    private void SelectColor(Color color, bool updateHex = true)
    {
        updating = true;
        SelectedColor = color;
        Red.Value = color.R;
        Green.Value = color.G;
        Blue.Value = color.B;
        var hex = Format(color);
        if (updateHex)
            Hex.Text = hex;
        Preview.Background = new SolidColorBrush(color);
        PreviewTitle.Foreground = PreviewText.Foreground = NoteViewModel.ContrastingForeground(color);
        HexError.Visibility = Visibility.Collapsed;
        ApplyColor.IsEnabled = true;
        SaveColor.IsEnabled = !favorites.Contains(hex);
        updating = false;
    }

    private void Channels_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!updating)
            SelectColor(Color.FromRgb((byte)Math.Round(Red.Value), (byte)Math.Round(Green.Value), (byte)Math.Round(Blue.Value)));
    }

    private void Hex_Changed(object sender, TextChangedEventArgs e)
    {
        if (updating)
            return;

        if (Parse(Hex.Text) is { } color)
        {
            SelectColor(color, updateHex: false);
        }
        else
        {
            HexError.Visibility = Visibility.Visible;
            ApplyColor.IsEnabled = SaveColor.IsEnabled = false;
        }
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex } && Parse(hex) is { } color)
            SelectColor(color);
    }

    private void SaveColor_Click(object sender, RoutedEventArgs e)
    {
        var hex = Format(SelectedColor);
        if (!saveColor(hex))
            return;

        favorites.Add(hex);
        EmptyPalette.Visibility = Visibility.Collapsed;
        SaveColor.IsEnabled = false;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private static string Format(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color? Parse(string text)
    {
        var digits = text.StartsWith('#') ? text.AsSpan(1) : text.AsSpan();
        if (digits.Length != 6 || !int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            return null;

        return Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
