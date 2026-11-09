using System;
using System.Windows;

namespace _stick;

internal enum TrashAnchor
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, Center, MiddleRight,
    BottomLeft, BottomCenter, BottomRight
}

internal sealed record TrashLayout
{
    public const int MinWidth = 148;
    public const int MaxWidth = 320;
    public const int MinHeight = 78;
    public const int MaxHeight = 180;
    public const int MinMargin = 0;
    public const int MaxMargin = 56;

    public TrashAnchor Anchor { get; init; } = TrashAnchor.BottomCenter;
    public double Width { get; init; } = 196;
    public double Height { get; init; } = 108;
    public double Margin { get; init; } = 24;

    public TrashLayout Normalize() => this with
    {
        Anchor = Enum.IsDefined(Anchor) ? Anchor : TrashAnchor.BottomCenter,
        Width = Math.Clamp(Width, MinWidth, MaxWidth),
        Height = Math.Clamp(Height, MinHeight, MaxHeight),
        Margin = Math.Clamp(Margin, MinMargin, MaxMargin)
    };

    public Rect GetBounds(Rect workArea)
    {
        var width = Math.Min(Width, workArea.Width);
        var height = Math.Min(Height, workArea.Height);
        var freeX = workArea.Width - width;
        var freeY = workArea.Height - height;
        var marginX = Math.Min(Margin, freeX);
        var marginY = Math.Min(Margin, freeY);
        var left = Anchor switch
        {
            TrashAnchor.TopLeft or TrashAnchor.MiddleLeft or TrashAnchor.BottomLeft => marginX,
            TrashAnchor.TopRight or TrashAnchor.MiddleRight or TrashAnchor.BottomRight => freeX - marginX,
            _ => freeX / 2
        };
        var top = Anchor switch
        {
            TrashAnchor.TopLeft or TrashAnchor.TopCenter or TrashAnchor.TopRight => marginY,
            TrashAnchor.BottomLeft or TrashAnchor.BottomCenter or TrashAnchor.BottomRight => freeY - marginY,
            _ => freeY / 2
        };
        return new Rect(workArea.X + left, workArea.Y + top, width, height);
    }
}
