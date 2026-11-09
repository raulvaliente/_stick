using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace _stick;

public partial class TrashSettingsWindow : Window
{
    private readonly App app;
    private TrashAnchor anchor;

    internal TrashSettingsWindow(App app)
    {
        this.app = app;
        InitializeComponent();
        SetRange(WidthSlider, TrashLayout.MinWidth, TrashLayout.MaxWidth);
        SetRange(HeightSlider, TrashLayout.MinHeight, TrashLayout.MaxHeight);
        SetRange(MarginSlider, TrashLayout.MinMargin, TrashLayout.MaxMargin);
        LoadValues(app.TrashLayout);

        foreach (var option in AnchorButtons.Children.OfType<RadioButton>())
            option.Checked += Anchor_Checked;
        WidthSlider.ValueChanged += Size_ValueChanged;
        HeightSlider.ValueChanged += Size_ValueChanged;
        MarginSlider.ValueChanged += Size_ValueChanged;
    }

    private static void SetRange(Slider slider, int minimum, int maximum)
    {
        slider.Maximum = maximum;
        slider.Minimum = minimum;
    }

    private void LoadValues(TrashLayout layout)
    {
        anchor = layout.Anchor;
        WidthSlider.Value = layout.Width;
        HeightSlider.Value = layout.Height;
        MarginSlider.Value = layout.Margin;
        foreach (var option in AnchorButtons.Children.OfType<RadioButton>())
            option.IsChecked = Equals(option.Tag, anchor.ToString());
        UpdatePreview();
    }

    private TrashLayout ReadLayout() => new()
    {
        Anchor = anchor,
        Width = WidthSlider.Value,
        Height = HeightSlider.Value,
        Margin = MarginSlider.Value
    };

    private void UpdatePreview()
    {
        var bounds = ReadLayout().GetBounds(new Rect(0, 0, PreviewArea.Width, PreviewArea.Height));
        Canvas.SetLeft(PreviewTarget, bounds.Left);
        Canvas.SetTop(PreviewTarget, bounds.Top);
        PreviewTarget.Width = bounds.Width;
        PreviewTarget.Height = bounds.Height;
    }

    private void Anchor_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string value } && Enum.TryParse<TrashAnchor>(value, out var selected))
        {
            anchor = selected;
            UpdatePreview();
        }
    }

    private void Size_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePreview();

    private void Reset_Click(object sender, RoutedEventArgs e) => LoadValues(new TrashLayout());

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (app.SetTrashLayout(ReadLayout()))
            DialogResult = true;
    }
}
