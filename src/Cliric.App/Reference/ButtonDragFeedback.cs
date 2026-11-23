using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Cliric.App.Reference;

internal sealed class ButtonDragFeedback
{
    private readonly FrameworkElement _hit;
    private readonly TranslateTransform _move;
    private readonly Func<bool> _enabled, _motion, _menuOpen;
    private readonly Action _click;
    private readonly Action<bool> _press;
    private long _entered;
    private bool _pressed;
    internal ButtonDragFeedback(FrameworkElement hit, FrameworkElement target, Func<bool> enabled, Func<bool> motion, Action click, Action<bool>? press = null, Func<bool>? menuOpen = null)
    {
        _hit = hit;
        _enabled = enabled;
        _motion = motion;
        _click = click;
        _press = press ?? (_ => { });
        _menuOpen = menuOpen ?? (() => false);
        _move = target.RenderTransform as TranslateTransform ?? new TranslateTransform();
        target.RenderTransform = _move;
        hit.IsHitTestVisible = enabled();
        hit.MouseEnter += (_, e) => { _entered = Environment.TickCount64; Move(e.GetPosition(hit), _entered); };
        hit.MouseMove += (_, e) => Move(e.GetPosition(hit), Environment.TickCount64);
        hit.MouseLeave += (_, _) => { if (!_pressed && !_menuOpen()) Return(false); };
        hit.MouseLeftButtonDown += (_, e) => { if (!_enabled() || !hit.CaptureMouse()) return; _pressed = true; _press(true); e.Handled = true; };
        hit.MouseLeftButtonUp += (_, e) =>
        {
            if (!_pressed)
                return;
            bool inside = new Rect(hit.RenderSize).Contains(e.GetPosition(hit));
            _pressed = false;
            hit.ReleaseMouseCapture();
            _press(false);
            if (inside)
                _click();
            if (!_menuOpen())
                Return(true);
            e.Handled = true;
        };
        hit.LostMouseCapture += (_, _) => { if (_pressed) { _pressed = false; _press(false); Return(true); } };
        hit.Unloaded += (_, _) => { _pressed = false; if (hit.IsMouseCaptured) hit.ReleaseMouseCapture(); Return(false); };
    }
    internal void Refresh()
    {
        bool enabled = _enabled();
        if (_hit.IsHitTestVisible == enabled)
            return;
        _hit.IsHitTestVisible = enabled;
        if (!enabled)
        {
            _pressed = false;
            _press(false);
            if (_hit.IsMouseCaptured)
                _hit.ReleaseMouseCapture();
            Return(false);
        }
    }
    internal void Reset() => Return(false);
    internal void Move(Point point, long now)
    {
        if (!_enabled())
            return;
        int duration = (int)Math.Clamp(250 - (now - _entered), 0, 250);
        Set((point.X - _hit.ActualWidth / 2) / 2.8, (point.Y - _hit.ActualHeight / 2) / 2.8, duration, new CubicEase { EasingMode = EasingMode.EaseOut });
    }
    private void Return(bool pressed) => Set(0, 0, pressed ? 1300 : 300, pressed ? new ElasticEase { Oscillations = 7, Springiness = 10, EasingMode = EasingMode.EaseOut } : new BackEase { Amplitude = .5, EasingMode = EasingMode.EaseOut });
    private void Set(double x, double y, int duration, IEasingFunction ease)
    {
        if (!_motion() || duration == 0)
        {
            _move.BeginAnimation(TranslateTransform.XProperty, null);
            _move.BeginAnimation(TranslateTransform.YProperty, null);
            _move.X = x;
            _move.Y = y;
            return;
        }
        _move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_move.X, x, TimeSpan.FromMilliseconds(duration)) { EasingFunction = ease });
        _move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_move.Y, y, TimeSpan.FromMilliseconds(duration)) { EasingFunction = ease });
    }
}
