using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopRect = System.Drawing.Rectangle;
using Forms = System.Windows.Forms;

namespace _stick;

public partial class NoteWindow : Window
{
    private readonly NoteViewModel viewModel;
    private readonly Action addNote;
    private readonly Action deleteNote;
    private readonly Func<NoteWindow[]> getFamily;
    private readonly Func<TrashLayout> getTrashLayout;
    private readonly Func<NoteWindow, Color?> pickColor;
    private Dictionary<NoteWindow, Point>? dragOrigins;
    private Point dragOrigin;
    private DpiScale dragDpi;
    private bool moveFamily;
    private bool hasMoved;
    private TrashDropWindow? trashDrop;

    internal NoteWindow(string collectionName, NoteViewModel viewModel, Action addNote, Action deleteNote,
        Func<NoteWindow[]> getFamily, Func<TrashLayout> getTrashLayout, Func<NoteWindow, Color?> pickColor)
    {
        this.viewModel = viewModel;
        this.addNote = addNote;
        this.deleteNote = deleteNote;
        this.getFamily = getFamily;
        this.getTrashLayout = getTrashLayout;
        this.pickColor = pickColor;
        InitializeComponent();
        Title = collectionName;
        DataContext = viewModel;
        var area = SystemParameters.WorkArea;
        var preferredWidth = viewModel.IsTutorial ? 780 : viewModel.Width;
        var preferredHeight = viewModel.IsTutorial ? 590 : viewModel.Height;
        Width = Math.Clamp(preferredWidth, MinWidth, Math.Max(MinWidth, area.Width));
        Height = Math.Clamp(preferredHeight, MinHeight, Math.Max(MinHeight, area.Height));
        if (viewModel.IsTutorial)
        {
            Editor.Visibility = Visibility.Collapsed;
            Preview.Visibility = Visibility.Collapsed;
            Tutorial.Content = viewModel.IsMarkdownGuide ? new MarkdownGuideView() : new WelcomeNoteView();
            Tutorial.Visibility = Visibility.Visible;
        }
        else
        {
            Preview.Attach(Editor);
            _ = new MarkdownEditing(Editor);
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

    internal Color NoteColor => viewModel.Color;

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (pickColor(this) is { } color)
            viewModel.SetColor(color);
    }

    internal void PlaceBeside(NoteWindow parent, IEnumerable<Window> occupied)
    {
        var parentHandle = new WindowInteropHelper(parent).Handle;
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(parentHandle, out var source) || !GetWindowRect(handle, out var current))
            return;

        const int gap = 10;
        var area = Forms.Screen.FromHandle(parentHandle).WorkingArea;
        var width = current.Right - current.Left;
        var height = current.Bottom - current.Top;
        var top = Math.Clamp(source.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
        var obstacles = occupied.Where(window => window != this && window.IsVisible)
            .Select(ScreenBounds).OfType<DesktopRect>().ToArray();

        var leftStart = source.Left - gap - width;
        var rightStart = source.Right + gap;
        var row = obstacles.Where(bounds => bounds.Top < top + height + gap && bounds.Bottom > top - gap);
        var leftSlots = row.Select(bounds => bounds.Left - gap - width).Append(leftStart)
            .Where(left => left <= leftStart).OrderDescending();
        var rightSlots = row.Select(bounds => bounds.Right + gap).Append(rightStart)
            .Where(left => left >= rightStart).Order();
        var alignedLeft = Math.Clamp(source.Left, area.Left, Math.Max(area.Left, area.Right - width));
        var candidates = leftSlots.Concat(rightSlots).Distinct().Select(left => new DesktopRect(left, top, width, height))
            .Append(new DesktopRect(alignedLeft, source.Bottom + gap, width, height))
            .Append(new DesktopRect(alignedLeft, source.Top - gap - height, width, height));

        foreach (var candidate in candidates)
        {
            var spacing = DesktopRect.Inflate(candidate, gap, gap);
            if (!area.Contains(candidate) || obstacles.Any(spacing.IntersectsWith))
                continue;

            MoveTo(handle, candidate.Left, candidate.Top);
            return;
        }
    }

    private static DesktopRect? ScreenBounds(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return GetWindowRect(handle, out var bounds)
            ? DesktopRect.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)
            : null;
    }

    internal void TrackPosition(int? savedLeft, int? savedTop)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (savedLeft is int left && savedTop is int top && GetWindowRect(handle, out var current))
        {
            // Screen and Win32 use desktop pixels, including monitors left of the primary display.
            var area = Forms.Screen.FromPoint(new System.Drawing.Point(left, top)).WorkingArea;
            var width = current.Right - current.Left;
            var height = current.Bottom - current.Top;
            var maxLeft = Math.Max(area.Left, area.Right - width);
            var maxTop = Math.Max(area.Top, area.Bottom - height);
            MoveTo(handle, Math.Clamp(left, area.Left, maxLeft), Math.Clamp(top, area.Top, maxTop));
        }

        RememberPosition(handle);
        LocationChanged += (_, _) => RememberPosition(handle);
    }

    private static void MoveTo(IntPtr handle, int left, int top)
    {
        const uint keepSizeAndOrder = 0x0001 | 0x0004 | 0x0010; // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
        SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, keepSizeAndOrder);
    }

    private void RememberPosition(IntPtr handle)
    {
        if (WindowState == WindowState.Normal && GetWindowRect(handle, out var bounds))
            viewModel.SetPosition(bounds.Left, bounds.Top);
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
        if (IsLoaded && !viewModel.IsTutorial && WindowState == WindowState.Normal)
            viewModel.SetSize(sizeInfo.NewSize.Width, sizeInfo.NewSize.Height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect bounds);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int left, int top, int width, int height, uint flags);
}
