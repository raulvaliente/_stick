using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace _stick;

public partial class NoteWindow : Window
{
    private readonly NoteViewModel viewModel;
    private readonly Action addNote;
    private readonly Action deleteNote;

    internal NoteWindow(string collectionName, NoteViewModel viewModel, Action addNote, Action deleteNote)
    {
        this.viewModel = viewModel;
        this.addNote = addNote;
        this.deleteNote = deleteNote;
        InitializeComponent();
        Title = collectionName;
        DataContext = viewModel;
        var area = SystemParameters.WorkArea;
        Width = Math.Clamp(viewModel.Width, MinWidth, Math.Max(MinWidth, area.Width));
        Height = Math.Clamp(viewModel.Height, MinHeight, Math.Max(MinHeight, area.Height));
        Preview.Attach(Editor);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NoteViewModel.Background) or nameof(NoteViewModel.Foreground))
                Preview.InvalidateVisual();
        };
    }

    private void New_Click(object sender, RoutedEventArgs e) => addNote();

    private void Hide_Click(object sender, RoutedEventArgs e) => Close();

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "¿Eliminar esta nota?", "_stick",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            deleteNote();
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        var color = ((SolidColorBrush)viewModel.Background).Color;
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B)
        };

        if (dialog.ShowDialog(new DialogOwner(new WindowInteropHelper(this).Handle)) == Forms.DialogResult.OK)
            viewModel.SetColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsLoaded && WindowState == WindowState.Normal)
            viewModel.SetSize(sizeInfo.NewSize.Width, sizeInfo.NewSize.Height);
    }

    private sealed record DialogOwner(IntPtr Handle) : Forms.IWin32Window;
}
