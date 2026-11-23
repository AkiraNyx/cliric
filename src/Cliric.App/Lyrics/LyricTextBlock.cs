using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Cliric.Core;

namespace Cliric.App;

public sealed class LyricTextBlock : TextBlock
{
    private Geometry? _hanContour;
    private bool _contourPending;

    public LyricTextBlock()
    {
        Loaded += (_, _) => ScheduleContour();
        SizeChanged += (_, _) => ScheduleContour();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ScheduleContour();
    }

    private void ScheduleContour()
    {
        if (_contourPending || !IsLoaded || (_hanContour is null && !NeedsHanContour()))
            return;

        _contourPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _contourPending = false;
            if (IsLoaded)
                ApplyHanContour();
        }));
    }

    private bool NeedsHanContour() => FontWeight == FontWeights.Bold && Text.Any(Timeline.IsHan);

    internal void ApplyHanContour()
    {
        if (Clip is not null && !ReferenceEquals(Clip, _hanContour))
            return;

        Geometry? next = null;
        if (NeedsHanContour() && VisualTreeHelper.GetDrawing(this) is { } nativeDrawing)
        {
            // Clip ink to the shaped outline while retaining the native text layout.
            var contour = new GeometryGroup { FillRule = FillRule.Nonzero };
            if (CollectContour(nativeDrawing, Matrix.Identity, contour))
            {
                contour.Freeze();
                next = contour;
            }
        }
        _hanContour = next;
        SetCurrentValue(ClipProperty, next);
    }

    private static bool CollectContour(Drawing drawing, Matrix transform, GeometryGroup contour)
    {
        if (drawing is DrawingGroup group)
        {
            Matrix position = group.Transform?.Value ?? Matrix.Identity;
            position.Append(transform);
            bool foundHan = false;
            foreach (var child in group.Children)
                foundHan |= CollectContour(child, position, contour);
            return foundHan;
        }

        bool han = drawing is GlyphRunDrawing { GlyphRun: { Characters: { } characters } } &&
            characters.Any(Timeline.IsHan) && characters.All(c => Timeline.IsHan(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c));
        Geometry shape;
        if (han)
        {
            shape = ((GlyphRunDrawing)drawing).GlyphRun.BuildGeometry();
        }
        else
        {
            var bounds = drawing.Bounds;
            if (bounds.IsEmpty)
                return false;
            bounds.Inflate(1, 1);
            shape = new RectangleGeometry(bounds);
        }

        if (shape.IsEmpty())
            return false;
        if (!transform.IsIdentity)
        {
            if (shape.IsFrozen)
                shape = shape.CloneCurrentValue();
            Matrix position = shape.Transform?.Value ?? Matrix.Identity;
            position.Append(transform);
            shape.Transform = new MatrixTransform(position);
        }
        contour.Children.Add(shape);
        return han;
    }
}
