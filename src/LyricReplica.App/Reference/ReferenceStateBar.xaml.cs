using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LyricReplica.Core;
using Timeline = LyricReplica.Core.Timeline;

namespace LyricReplica.App.Reference;

public partial class ReferenceStateBar : UserControl
{
    public static readonly DependencyProperty CurrentTimeProperty = DependencyProperty.Register(nameof(CurrentTime), typeof(int), typeof(ReferenceStateBar), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((ReferenceStateBar)d).UpdateTime()));
    public static readonly DependencyProperty TotalTimeProperty = DependencyProperty.Register(nameof(TotalTime), typeof(int), typeof(ReferenceStateBar), new PropertyMetadata(5400000, (d, e) => ((ReferenceStateBar)d).UpdateTime()));
    public static readonly DependencyProperty ForceWidthProperty = DependencyProperty.Register(nameof(ForceWidth), typeof(double), typeof(ReferenceStateBar), new PropertyMetadata(double.NaN, (d, e) => { var bar = (ReferenceStateBar)d; if (bar.Grid is not null) bar.Grid.Width = double.IsNaN((double)e.NewValue) ? 800 : (double)e.NewValue; }));
    public static readonly DependencyProperty ElasticOffsetProperty = DependencyProperty.Register(nameof(ElasticOffset), typeof(double), typeof(ReferenceStateBar), new PropertyMetadata(0d, (d, e) => ((ReferenceStateBar)d).ApplyElastic()));
    public static readonly DependencyProperty HoverFactorProperty = DependencyProperty.Register(nameof(HoverFactor), typeof(double), typeof(ReferenceStateBar), new PropertyMetadata(1d, (d, e) => ((ReferenceStateBar)d).ApplyHover()));
    public int CurrentTime
    {
        get => (int)GetValue(CurrentTimeProperty); set => SetValue(CurrentTimeProperty, value);
    }
    public int TotalTime
    {
        get => (int)GetValue(TotalTimeProperty); set => SetValue(TotalTimeProperty, value);
    }
    public double ForceWidth
    {
        get => (double)GetValue(ForceWidthProperty); set => SetValue(ForceWidthProperty, value);
    }
    public bool IsRealTimeUpdate
    {
        get; set;
    }
    public bool ReduceMotion
    {
        get; set;
    }
    private RectangleGeometry? _shellFill;
    private (int Current, int Remaining, bool Rolling)? _shellReadout;
    private bool ShellInteraction => _shellFill is not null;
    private bool _animatedTime = true;
    public bool AnimatedTime
    {
        get => _animatedTime; set
        {
            _animatedTime = value;
            UpdateTime();
        }
    }
    public double ElasticOffset
    {
        get => (double)GetValue(ElasticOffsetProperty); private set => SetValue(ElasticOffsetProperty, value);
    }
    private double HoverFactor
    {
        get => (double)GetValue(HoverFactorProperty); set => SetValue(HoverFactorProperty, value);
    }
    private bool? _elasticSide;
    public bool Dragging
    {
        get; private set;
    }
    public event Action<double>? SeekRequested;
    private double _preview;
    public ReferenceStateBar()
    {
        InitializeComponent();
        BorderCover.MouseEnter += (_, e) => { if (!ShellInteraction || e.StylusDevice is null) AnimateHeight(2); };
        BorderCover.MouseLeave += (_, _) =>
        {
            if (ShellInteraction && Dragging)
                return;
            AnimateHeight(1);
            if (Dragging)
                Commit();
        };
        BorderCover.MouseLeftButtonDown += (_, e) =>
        {
            if (!BorderCover.CaptureMouse() && ShellInteraction)
                return;
            Focus();
            BeginPreview(e.GetPosition(BorderCover).X);
            e.Handled = true;
        };
        BorderCover.MouseMove += (_, e) => { if (Dragging) Preview(e.GetPosition(BorderCover).X); };
        BorderCover.MouseLeftButtonUp += (_, e) => { if (Dragging) { Preview(e.GetPosition(BorderCover).X); Commit(); e.Handled = true; } };
        BorderCover.LostMouseCapture += (_, _) => { if (Dragging) CancelPreview(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && Dragging) { CancelPreview(); e.Handled = true; } else if (e.Key is Key.Left or Key.Right) { SeekRequested?.Invoke(Math.Clamp(CurrentTime + (e.Key == Key.Left ? -5000 : 5000), 0, TotalTime)); e.Handled = true; } };
        UpdateTime();
    }

    internal void UseShellLayout()
    {
        _shellFill = new RectangleGeometry();
        Grid.Opacity = 1;
        FocusVisualStyle = (Style)FindResource("Shell.KeyboardFocus");
        ApplyShellPalette();
        BorderCover.Margin = GridBorders.Margin;
        GridPlayed.Margin = new Thickness(0);
        GridPlayed.HorizontalAlignment = HorizontalAlignment.Stretch;
        GridPlayed.Clip = _shellFill;
        GridBorders.RenderTransformOrigin = new Point(.5, .5);
        GridBorders.SizeChanged += (_, _) => UpdateTime();
        UpdateTime();
    }

    internal void ApplyShellPalette()
    {
        Brush text = SystemParameters.HighContrast ? SystemColors.ControlTextBrush : Brushes.White;
        BorderPlayed.Background = SystemParameters.HighContrast ? text : (Brush)FindResource("Shell.Primary");
        BorderBackground.Background = text;
        TextBlockCurrentTime.Foreground = TextBlockLeftTime.Foreground = text;
        TimerViewCurrentTime.Foreground = TimerViewLeftTime.Foreground = text;
    }

    internal void SetShellReducedMotion(bool reduced)
    {
        if (ReduceMotion == reduced)
            return;
        ReduceMotion = reduced;
        if (reduced)
            ShellTransitions.Set(this, ElasticOffsetProperty, 0);
        AnimateHeight(BorderCover.IsMouseOver || Dragging ? 2 : 1);
        UpdateTime();
    }

    private void AnimateHeight(double value)
    {
        if (ShellInteraction)
        {
            bool focused = value > 1;
            ShellTransitions.To(GridBordersScaleTransform, ScaleTransform.ScaleYProperty, focused && !ReduceMotion ? 1.55 : 1,
                MotionTokens.SeekHover, ReduceMotion);
            ShellTransitions.To(BorderBackground, OpacityProperty, focused ? .72 : .5,
                MotionTokens.SeekHover, ReduceMotion);
            return;
        }
        if (!ShellInteraction)
            GridBorders.RenderTransformOrigin = new Point(.5, .5);
        if (GridBorders.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform();
            GridBorders.RenderTransform = scale;
        }
        if (ReduceMotion)
        {
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleY = value;
            BeginAnimation(HoverFactorProperty, null);
            HoverFactor = value;
        }
        else
        {
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            BeginAnimation(HoverFactorProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
    public void BeginPreview(double x)
    {
        Dragging = true;
        Preview(x);
    }
    public void MovePreview(double x)
    {
        if (Dragging)
            Preview(x);
    }
    public void CommitPreview() => Commit();
    public void CancelPreview()
    {
        if (!Dragging)
            return;
        Dragging = false;
        if (BorderCover.IsMouseCaptured)
            BorderCover.ReleaseMouseCapture();
        ResetElastic();
    }
    private void Preview(double x)
    {
        var frame = SeekMotion.Preview(x, ShellInteraction ? BorderCover.ActualWidth : GridBorders.ActualWidth, TotalTime);
        _preview = frame.TimeMs;
        _elasticSide = frame.Elastic == 0 ? null : frame.Elastic > 0;
        if (ShellInteraction)
            ShellTransitions.Set(this, ElasticOffsetProperty, ReduceMotion ? 0 : Math.Clamp(frame.Elastic, -36, 36));
        else
        {
            BeginAnimation(ElasticOffsetProperty, null);
            ElasticOffset = frame.Elastic;
            ApplyElastic();
        }
        SetCurrentValue(CurrentTimeProperty, frame.TimeMs);
        if (IsRealTimeUpdate)
            SeekRequested?.Invoke(_preview);
    }
    private void Commit()
    {
        if (!Dragging)
            return;
        Dragging = false;
        if (BorderCover.IsMouseCaptured)
            BorderCover.ReleaseMouseCapture();
        ResetElastic();
        SeekRequested?.Invoke(CurrentTime);
    }
    private void ResetElastic()
    {
        if (ShellInteraction)
        {
            ShellTransitions.To(this, ElasticOffsetProperty, 0, MotionTokens.SeekRelease, ReduceMotion);
            if (!BorderCover.IsMouseOver)
                AnimateHeight(1);
            return;
        }
        if (ReduceMotion)
        {
            BeginAnimation(ElasticOffsetProperty, null);
            ElasticOffset = 0;
            ApplyElastic();
        }
        else
            BeginAnimation(ElasticOffsetProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(800)) { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5.5, EasingMode = EasingMode.EaseOut } });
    }
    private void ApplyElastic()
    {
        if (ShellInteraction)
        {
            GridBorders.RenderTransformOrigin = new Point(ElasticOffset < 0 ? 1 : 0, .5);
            GridBordersScaleTransform.ScaleX = 1 + Math.Abs(ElasticOffset) / Math.Max(1, GridBorders.ActualWidth);
            return;
        }
        if (GridBorders is not null)
            GridBorders.Margin = _elasticSide == false ? new Thickness(16 + ElasticOffset, -40, 16, 0) : _elasticSide == true ? new Thickness(16, -40, 16 - ElasticOffset, 0) : new Thickness(16, -40, 16, 0);
    }
    private void ApplyHover()
    {
        if (Grid is null)
            return;
        Grid.Opacity = .5 + (HoverFactor - 1) / 4;
        BorderBackground.CornerRadius = BorderPlayed.CornerRadius = new CornerRadius(HoverFactor * 9);
    }
    public void SetPosition(double position, double total)
    {
        SetCurrentValue(TotalTimeProperty, (int)Math.Max(0, total));
        if (!Dragging)
            SetCurrentValue(CurrentTimeProperty, (int)Math.Max(0, position));
    }
    private void UpdateTime()
    {
        if (GridPlayed is null || TextBlockCurrentTime is null)
            return;
        double fraction = TotalTime <= 0 ? 1 : Math.Min((double)CurrentTime / TotalTime, 1);
        bool rolling = _animatedTime && Math.Max(CurrentTime, TotalTime) < 6000000;
        if (_shellFill is { } fill)
        {
            fill.Rect = new Rect(0, 0, Math.Clamp(fraction, 0, 1) * GridBorders.ActualWidth, GridBorders.ActualHeight);
            rolling &= !ReduceMotion;
            var readout = (CurrentTime / 1000, Math.Max(0, TotalTime - CurrentTime) / 1000, rolling);
            if (_shellReadout == readout)
                return;
            _shellReadout = readout;
        }
        else
            GridPlayed.Margin = new Thickness(0, 0, (1 - fraction) * GridBorders.ActualWidth, 0);
        TextBlockCurrentTime.Text = Timeline.Format(CurrentTime);
        TextBlockLeftTime.Text = TotalTime <= 0 ? "-0:00" : "-" + Timeline.Format(Math.Max(1, TotalTime - CurrentTime));
        TimerViewCurrentTime.Animate = TimerViewLeftTime.Animate = rolling && !ReduceMotion;
        TextBlockCurrentTime.Opacity = TextBlockLeftTime.Opacity = rolling ? 0 : .5;
        TimerViewCurrentTime.Opacity = TimerViewLeftTime.Opacity = rolling ? .5 : 0;
        TimerViewCurrentTime.Time = TimeSpan.FromMilliseconds(Math.Max(0, CurrentTime));
        TimerViewLeftTime.Time = TimeSpan.FromMilliseconds(-Math.Max(1, TotalTime - CurrentTime));
    }
}
