using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace LyricReplica.App;

public static class ShellAssist
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(ShellAssist), new PropertyMetadata(new CornerRadius(8)));
    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);
}

/// <summary>The existing slide into and out of the lyrics page.</summary>
internal static class ShellMotion
{
    public static readonly IEasingFunction Out = Frozen(new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut });

    public static Func<bool> Reduced { get; set; } = () => false;

    public static void To(IAnimatable target, DependencyProperty property, double to, double ms, IEasingFunction? easing = null, double delayMs = 0, double? from = null, Action? completed = null)
    {
        if (Reduced() || ms <= 0)
        {
            target.BeginAnimation(property, null);
            ((DependencyObject)target).SetValue(property, to);
            completed?.Invoke();
            return;
        }
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = easing ?? Out,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            FillBehavior = FillBehavior.HoldEnd
        };
        if (from is { } start)
            animation.From = start;
        if (completed is not null)
            animation.Completed += (_, _) => completed();
        target.BeginAnimation(property, animation);
    }

    private static IEasingFunction Frozen(EasingFunctionBase easing)
    {
        easing.Freeze();
        return easing;
    }
}

public class ShellScrollViewer : ScrollViewer
{
    private double? _wheelTarget;
    private double _wheelOffset;
    private double _lastWheelOffset;
    private TimeSpan _lastWheelFrame;
    private long _lastWheelTick;
    internal bool IsWheelAnimating => _wheelTarget.HasValue;
    protected virtual bool ReduceWheelMotion => ShellTransitions.GetReducedMotion(this);
    protected virtual double WheelLineHeight => 24;

    public ShellScrollViewer() => Unloaded += (_, _) => StopWheelAnimation();

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        int lines = SystemParameters.WheelScrollLines;
        if (ReduceWheelMotion || lines == 0 || ScrollableHeight <= 0)
        {
            StopWheelAnimation();
            base.OnMouseWheel(e);
            return;
        }

        double step = lines < 0 ? ViewportHeight : WheelLineHeight * lines;
        double target = Math.Clamp((_wheelTarget ?? VerticalOffset) - e.Delta * step / 120, 0, ScrollableHeight);
        e.Handled = true;
        if (target == VerticalOffset && _wheelTarget is null)
            return;
        if (_wheelTarget is null)
        {
            _wheelOffset = _lastWheelOffset = VerticalOffset;
            _lastWheelFrame = TimeSpan.Zero;
            _lastWheelTick = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnWheelFrame;
        }
        _wheelTarget = target;
    }

    private void OnWheelFrame(object? sender, EventArgs e)
    {
        if (_wheelTarget is not { } target || e is not RenderingEventArgs frame || frame.RenderingTime == _lastWheelFrame)
            return;
        long now = Stopwatch.GetTimestamp();
        double seconds = Stopwatch.GetElapsedTime(_lastWheelTick, now).TotalSeconds;
        _lastWheelTick = now;
        _lastWheelFrame = frame.RenderingTime;
        target = Math.Clamp(target, 0, ScrollableHeight);
        _wheelOffset += (target - _wheelOffset) * (1 - Math.Exp(-22 * Math.Max(0, seconds)));
        double pixel = 1 / VisualTreeHelper.GetDpi(this).DpiScaleY;
        if (Math.Abs(_wheelOffset - target) < .5 * pixel)
        {
            StopWheelAnimation();
            SetFrameOffset(target);
        }
        else if (Math.Abs(_wheelOffset - _lastWheelOffset) >= .5 * pixel)
        {
            _lastWheelOffset = _wheelOffset;
            SetFrameOffset(_wheelOffset);
        }
    }

    internal void SetFrameOffset(double offset)
    {
        if (ScrollInfo is { } info)
            info.SetVerticalOffset(offset);
        else
            ScrollToVerticalOffset(offset);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        StopWheelAnimation();
        base.OnPreviewMouseDown(e);
    }

    internal void StopWheelAnimation()
    {
        if (_wheelTarget is null)
            return;
        _wheelTarget = null;
        CompositionTarget.Rendering -= OnWheelFrame;
    }

    internal void FinishWheelAnimation()
    {
        if (_wheelTarget is not { } target)
            return;
        StopWheelAnimation();
        SetFrameOffset(Math.Clamp(target, 0, ScrollableHeight));
    }
}

/// <summary>An open ring that turns while work is in progress.</summary>
internal sealed class Spinner : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Spinner),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));
    private readonly RotateTransform _turn = new();
    private static readonly DoubleAnimation Cycle = MotionTokens.Freeze(new DoubleAnimation(0, 360,
        TimeSpan.FromMilliseconds(MotionTokens.BusyCycleMs)) { RepeatBehavior = RepeatBehavior.Forever });
    private bool _spinning;

    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public Spinner()
    {
        RenderTransformOrigin = new Point(.5, .5);
        RenderTransform = _turn;
        Loaded += (_, _) => RefreshMotion();
        Unloaded += (_, _) => SetSpinning(false);
        IsVisibleChanged += (_, _) => RefreshMotion();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ShellTransitions.ReducedMotionProperty && IsLoaded)
            RefreshMotion();
    }

    private void RefreshMotion() => SetSpinning(IsLoaded && IsVisible && !ShellTransitions.GetReducedMotion(this));

    private void SetSpinning(bool spinning)
    {
        if (_spinning == spinning)
            return;
        _spinning = spinning;
        _turn.BeginAnimation(RotateTransform.AngleProperty, spinning ? Cycle : null);
    }

    protected override void OnRender(DrawingContext context)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
            return;
        double thickness = Math.Max(1.5, size / 9), radius = (size - thickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(Math.PI * 1.5), center.Y - radius * Math.Cos(Math.PI * 1.5));
        var arc = new StreamGeometry();
        using (var stream = arc.Open())
        {
            stream.BeginFigure(start, false, false);
            stream.ArcTo(end, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true, true);
        }
        context.DrawGeometry(null, new Pen(Foreground, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
    }
}

/// <summary>A steady indicator for the active track's playback state.</summary>
internal sealed class EqualizerBars : Canvas
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(EqualizerBars),
        new PropertyMetadata(false, (d, _) => ((EqualizerBars)d).Refresh()));
    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(EqualizerBars),
        new PropertyMetadata(Brushes.White, (d, e) => { foreach (var bar in ((EqualizerBars)d)._bars) bar.Fill = (Brush)e.NewValue; }));
    private readonly Rectangle[] _bars = new Rectangle[3];
    private static readonly double[] Rests = [.35, .6, .45], Playing = [.55, 1, .72];

    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public Brush BarBrush { get => (Brush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }

    public EqualizerBars()
    {
        Width = 14;
        Height = 12;
        for (int i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Rectangle { Width = 3, Height = 12, RadiusX = 1, RadiusY = 1, Fill = BarBrush, RenderTransformOrigin = new Point(.5, 1), RenderTransform = new ScaleTransform(1, Rests[i]) };
            SetLeft(_bars[i], i * 5.5);
            Children.Add(_bars[i]);
        }
    }

    private void Refresh()
    {
        for (int i = 0; i < _bars.Length; i++)
            ((ScaleTransform)_bars[i].RenderTransform).ScaleY = (IsActive ? Playing : Rests)[i];
    }
}
