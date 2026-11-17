using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LyricReplica.App;

public sealed class ShellSlider : Slider
{
    private Track? _track;

    public override void OnApplyTemplate()
    {
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        base.OnApplyTemplate();
        _track = GetTemplateChild("PART_Track") as Track;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsMoveToPointEnabled && _track is { Thumb: { IsMouseOver: false } } track && CaptureMouse())
        {
            Focus();
            SetTrackPosition(track, e.GetPosition(track));
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured || _track is not { } track)
            return;
        if (e.LeftButton == MouseButtonState.Pressed)
            SetTrackPosition(track, e.GetPosition(track));
        else
            ReleaseMouseCapture();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured && _track is { } track)
        {
            SetTrackPosition(track, e.GetPosition(track));
            ReleaseMouseCapture();
            e.Handled = true;
        }
        base.OnMouseLeftButtonUp(e);
    }

    private void SetTrackPosition(Track track, Point point)
    {
        if (track.Thumb is not { } thumb)
            return;
        bool horizontal = Orientation == Orientation.Horizontal;
        double thumbLength = horizontal ? thumb.ActualWidth : thumb.ActualHeight;
        double travel = (horizontal ? track.ActualWidth : track.ActualHeight) - thumbLength;
        if (travel <= 0)
            return;
        double fraction = ((horizontal ? point.X : point.Y) - thumbLength / 2) / travel;
        if (IsDirectionReversed == horizontal)
            fraction = 1 - fraction;
        double value = Minimum + Math.Clamp(fraction, 0, 1) * (Maximum - Minimum);
        if (IsSnapToTickEnabled && TickFrequency > 0)
            value = Minimum + Math.Round((value - Minimum) / TickFrequency) * TickFrequency;
        SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
    }

    internal void AttachValueEditor(TextBox editor, int decimals)
    {
        string format = decimals == 0 ? "0" : "0." + new string('#', decimals);
        bool edited = false;
        Refresh();
        editor.TextChanged += (_, _) => edited = true;
        ValueChanged += (_, _) =>
        {
            if (!editor.IsKeyboardFocusWithin || !edited)
                Refresh();
        };
        editor.GotKeyboardFocus += (_, _) => editor.SelectAll();
        editor.LostKeyboardFocus += (_, _) => Commit();
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Commit();
            else if (e.Key == Key.Escape)
                Refresh();
            else
                return;
            editor.SelectAll();
            e.Handled = true;
        };

        void Commit()
        {
            if (edited && double.TryParse(editor.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) &&
                double.IsFinite(value))
                SetCurrentValue(ValueProperty, Math.Clamp(Math.Round(value, decimals), Minimum, Maximum));
            Refresh();
        }

        void Refresh()
        {
            editor.Text = Value.ToString(format, CultureInfo.CurrentCulture);
            edited = false;
        }
    }
}
