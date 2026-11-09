using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
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
    private readonly Func<NoteWindow[]> getFamily;
    private readonly Func<TrashLayout> getTrashLayout;
    private Dictionary<NoteWindow, Point>? dragOrigins;
    private Point dragOrigin;
    private DpiScale dragDpi;
    private bool moveFamily;
    private bool hasMoved;
    private TrashDropWindow? trashDrop;

    internal NoteWindow(string collectionName, NoteViewModel viewModel, Action addNote, Action deleteNote,
        Func<NoteWindow[]> getFamily, Func<TrashLayout> getTrashLayout)
    {
        this.viewModel = viewModel;
        this.addNote = addNote;
        this.deleteNote = deleteNote;
        this.getFamily = getFamily;
        this.getTrashLayout = getTrashLayout;
        InitializeComponent();
        Title = collectionName;
        DataContext = viewModel;
        var area = SystemParameters.WorkArea;
        var preferredWidth = viewModel.IsWelcome ? 780 : viewModel.Width;
        var preferredHeight = viewModel.IsWelcome ? 590 : viewModel.Height;
        Width = Math.Clamp(preferredWidth, MinWidth, Math.Max(MinWidth, area.Width));
        Height = Math.Clamp(preferredHeight, MinHeight, Math.Max(MinHeight, area.Height));
        if (viewModel.IsWelcome)
        {
            Editor.Visibility = Visibility.Collapsed;
            Preview.Visibility = Visibility.Collapsed;
            WelcomeGuide.Visibility = Visibility.Visible;
        }
        else
        {
            Preview.Attach(Editor);
        }
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
        if (MessageBox.Show(this, "¿Mover esta nota a la papelera?", "_stick",
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

    private static Point CursorOnScreen()
    {
        var point = Forms.Cursor.Position;
        return new Point(point.X, point.Y);
    }

    private void Header_DragStarted(object sender, DragStartedEventArgs e) =>
        StartDrag((Keyboard.Modifiers & ModifierKeys.Control) != 0);

    private void StartDrag(bool withControl)
    {
        moveFamily = withControl;
        hasMoved = false;
        dragOrigin = CursorOnScreen();
        dragDpi = VisualTreeHelper.GetDpi(this);
        dragOrigins = new Dictionary<NoteWindow, Point>();

        NoteWindow[] windows = moveFamily ? getFamily() : [this];
        foreach (var window in windows)
            dragOrigins[window] = new Point(window.Left, window.Top);
    }

    private void Header_DragDelta(object sender, DragDeltaEventArgs e) =>
        MoveDrag(CursorOnScreen());

    private void MoveDrag(Point cursor)
    {
        if (dragOrigins is null)
            return;

        var dx = (cursor.X - dragOrigin.X) / dragDpi.DpiScaleX;
        var dy = (cursor.Y - dragOrigin.Y) / dragDpi.DpiScaleY;
        if (!hasMoved)
        {
            if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
                return;

            hasMoved = true;
            if (!moveFamily)
                ShowTrash();
        }

        foreach (var (window, origin) in dragOrigins)
        {
            if (!window.IsVisible)
                continue;

            window.Left = origin.X + dx;
            window.Top = origin.Y + dy;
        }

        if (trashDrop is not null)
            trashDrop.Highlight(trashDrop.Contains(cursor));
    }

    private void ShowTrash()
    {
        var area = Forms.Screen.FromPoint(
            new System.Drawing.Point((int)dragOrigin.X, (int)dragOrigin.Y)).WorkingArea;
        var start = PointFromScreen(new Point(area.Left, area.Top));
        var end = PointFromScreen(new Point(area.Right, area.Bottom));
        var workArea = new Rect(Left + start.X, Top + start.Y, end.X - start.X, end.Y - start.Y);
        var bounds = getTrashLayout().GetBounds(workArea);
        trashDrop = new TrashDropWindow
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height
        };
        trashDrop.Show();
    }

    private void Header_DragCompleted(object sender, DragCompletedEventArgs e) =>
        FinishDrag(e.Canceled, CursorOnScreen());

    private void FinishDrag(bool canceled, Point cursor)
    {
        var dropped = !canceled && trashDrop?.Contains(cursor) == true;
        if (canceled && dragOrigins is not null)
        {
            foreach (var (window, origin) in dragOrigins)
            {
                if (!window.IsVisible)
                    continue;

                window.Left = origin.X;
                window.Top = origin.Y;
            }
        }

        ClearDrag();
        if (dropped)
            deleteNote();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DragHeader.IsDragging)
        {
            e.Handled = true;
            DragHeader.CancelDrag();
        }
    }

    private void ClearDrag()
    {
        dragOrigins = null;
        trashDrop?.Close();
        trashDrop = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        ClearDrag();
        base.OnClosed(e);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsLoaded && !viewModel.IsWelcome && WindowState == WindowState.Normal)
            viewModel.SetSize(sizeInfo.NewSize.Width, sizeInfo.NewSize.Height);
    }

    private sealed record DialogOwner(IntPtr Handle) : Forms.IWin32Window;
}
