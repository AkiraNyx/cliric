using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Cliric.App.Reference;

public static class InputMap
{
    public static readonly DependencyProperty ClickProperty = DependencyProperty.RegisterAttached("Click", typeof(string), typeof(InputMap), new PropertyMetadata(""));
    public static readonly DependencyProperty ValueChangedProperty = DependencyProperty.RegisterAttached("ValueChanged", typeof(string), typeof(InputMap), new PropertyMetadata(""));
    public static void SetClick(DependencyObject d, string value) => d.SetValue(ClickProperty, value);
    public static string GetClick(DependencyObject d) => (string)d.GetValue(ClickProperty);
    public static void SetValueChanged(DependencyObject d, string value) => d.SetValue(ValueChangedProperty, value);
    public static string GetValueChanged(DependencyObject d) => (string)d.GetValue(ValueChangedProperty);
}

public sealed class ArtworkImage : Image
{
    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(nameof(Url), typeof(ImageSource), typeof(ArtworkImage), new PropertyMetadata(null, (d, e) => ((ArtworkImage)d).SetCurrentValue(SourceProperty, e.NewValue)));
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(ArtworkImage), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public ImageSource? Url
    {
        get => (ImageSource?)GetValue(UrlProperty); set => SetValue(UrlProperty, value);
    }
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value);
    }
    public bool Fill
    {
        get => Stretch == Stretch.UniformToFill; set => Stretch = value ? Stretch.UniformToFill : Stretch.Uniform;
    }
    public bool IsAnimationEnabled
    {
        get; set;
    }
    public ArtworkImage()
    {
        Stretch = Stretch.UniformToFill;
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == SourceProperty && !Equals(Url, Source))
            SetCurrentValue(UrlProperty, Source);
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushClip(SquircleGeometry.Create(RenderSize.Width, RenderSize.Height, CornerRadius));
        base.OnRender(dc);
        dc.Pop();
    }
}

public sealed class TouchScrollViewer : ShellScrollViewer
{
    public bool IsInertiaEnabled { get; set; } = true;
    internal Func<bool> WheelMotionReduced { get; set; } = () => false;
    protected override bool ReduceWheelMotion => WheelMotionReduced();
    protected override double WheelLineHeight => 16;
    public TouchScrollViewer()
    {
        IsManipulationEnabled = true;
        PanningMode = PanningMode.VerticalOnly;
        AddHandler(ManipulationDeltaEvent, new EventHandler<ManipulationDeltaEventArgs>((_, e) => { if (e.IsInertial && !IsInertiaEnabled) e.Complete(); }), true);
    }
}

public sealed class ScrollSpring : EasingFunctionBase
{
    public ScrollSpring()
    {
        EasingMode = EasingMode.EaseIn;
    }
    protected override double EaseInCore(double time) => Cliric.Core.Motion.Scroll(time);
    protected override Freezable CreateInstanceCore() => new ScrollSpring();
}

public sealed class GridSpacing : Grid
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(GridSpacing), new PropertyMetadata(1d, UpdateWidth));
    public static readonly DependencyProperty WidthBindingProperty = DependencyProperty.Register(nameof(WidthBinding), typeof(double), typeof(GridSpacing), new PropertyMetadata(0d, UpdateWidth));
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value);
    }
    public double WidthBinding
    {
        get => (double)GetValue(WidthBindingProperty); set => SetValue(WidthBindingProperty, value);
    }
    private static void UpdateWidth(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (GridSpacing)d;
        grid.Width = Math.Max(0, grid.Spacing * grid.WidthBinding);
    }
}

public sealed class SpacedStackPanel : StackPanel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(SpacedStackPanel), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value);
    }
    protected override Size MeasureOverride(Size available)
    {
        double width = 0, height = 0;
        int count = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
            count++;
        }
        return new Size(width, height + Math.Max(0, count - 1) * Spacing);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height + Spacing;
        }
        return finalSize;
    }
}

public class ImageButton : Button
{
    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(ImageButton), new PropertyMetadata(null, (d, e) => ((ImageButton)d)._image.Source = (ImageSource?)e.NewValue));
    public static readonly DependencyProperty ImageMarginProperty = DependencyProperty.Register(nameof(ImageMargin), typeof(Thickness), typeof(ImageButton), new PropertyMetadata(new Thickness(0), (d, e) => ((ImageButton)d)._image.Margin = (Thickness)e.NewValue));
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty); set => SetValue(ImageSourceProperty, value);
    }
    public Thickness ImageMargin
    {
        get => (Thickness)GetValue(ImageMarginProperty); set => SetValue(ImageMarginProperty, value);
    }
    public double IconOpacity
    {
        get => _image.Opacity; set => _image.Opacity = value;
    }
    private bool _feedbackPressed, _feedbackMotion = true;
    internal bool FeedbackMotionEnabled
    {
        get => _feedbackMotion;
        set
        {
            _feedbackMotion = value;
            if (!value)
            {
                _feedbackPressed = false;
                if (FeedbackScale is { } scale)
                    InteractionFeedback.Scale(scale, 1, 1, 0, false);
                if (FeedbackBackground is { } background)
                    InteractionFeedback.Opacity(background, IsMouseOver ? .1 : .0001, false);
            }
        }
    }
    private Grid? FeedbackScale => (Content as FrameworkElement)?.FindName("GridScale") as Grid;
    private Border? FeedbackBackground => (Content as FrameworkElement)?.FindName("BorderBackground") as Border;
    public ImageButton()
    {
        var view = (UserControl)Application.LoadComponent(new Uri("/Cliric;component/Assets/ReferenceImageButton.xaml", UriKind.Relative));
        view.DataContext = this;
        _image = (Image)view.FindName("Image");
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        Cursor = Cursors.Hand;
        Content = view;
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        frame.AppendChild(presenter);
        Template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
        RenderTransformOrigin = new Point(.5, .5);
    }
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        if (FeedbackBackground is { } background)
            InteractionFeedback.Opacity(background, .1, FeedbackMotionEnabled);
        base.OnMouseEnter(e);
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (FeedbackBackground is { } background)
            InteractionFeedback.Opacity(background, .0001, FeedbackMotionEnabled);
        ReleaseFeedback();
        base.OnMouseLeave(e);
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (FeedbackScale is { } scale)
        {
            _feedbackPressed = true;
            InteractionFeedback.Scale(scale, 1, .88, 500, FeedbackMotionEnabled);
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseFeedback();
        if (e.StylusDevice is not null && FeedbackBackground is { } background)
            InteractionFeedback.Opacity(background, .0001, FeedbackMotionEnabled);
        base.OnPreviewMouseLeftButtonUp(e);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        ReleaseFeedback();
        base.OnLostMouseCapture(e);
    }
    private void ReleaseFeedback()
    {
        if (!_feedbackPressed)
            return;
        _feedbackPressed = false;
        if (FeedbackScale is { } scale)
            InteractionFeedback.Scale(scale, .88, 1, 500, FeedbackMotionEnabled);
    }
}
public sealed class NextButton : ImageButton
{
    private readonly UserControl _view = (UserControl)Application.LoadComponent(new Uri("/Cliric;component/Assets/ReferenceNext.xaml", UriKind.Relative));
    public NextButton()
    {
        Content = _view;
    }
    protected override void OnClick()
    {
        var host = (Viewbox)_view.FindName("RootGrid");
        ((Storyboard)host.Resources["Animation1"]).Begin(_view, HandoffBehavior.SnapshotAndReplace);
        base.OnClick();
    }
}
public sealed class FavoriteButton : ImageButton
{
    private readonly UserControl _view = (UserControl)Application.LoadComponent(new Uri("/Cliric;component/Assets/ReferenceFavorite.xaml", UriKind.Relative));
    private Storyboard? _favoriteAnimation;
    private bool _motionEnabled = true;
    internal bool MotionEnabled
    {
        get => _motionEnabled; set
        {
            if (_motionEnabled == value)
                return;
            _motionEnabled = value;
            if (!value)
            {
                ResetVisual();
                SetPressedVisual(false);
            }
        }
    }
    public static readonly DependencyProperty IsFavoritedProperty = DependencyProperty.Register(nameof(IsFavorited), typeof(bool), typeof(FavoriteButton), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((FavoriteButton)d).ResetVisual()));
    public bool IsFavorited
    {
        get => (bool)GetValue(IsFavoritedProperty); set => SetValue(IsFavoritedProperty, value);
    }
    public FavoriteButton()
    {
        Content = _view;
        Unloaded += (_, _) => { ResetVisual(); SetPressedVisual(false); };
        ResetVisual();
    }
    internal void Activate() => OnClick();
    protected override void OnClick()
    {
        SetCurrentValue(IsFavoritedProperty, !IsFavorited);
        base.OnClick();
        if (IsFavorited && MotionEnabled)
            StartFavoriteAnimation();
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        SetPressedVisual(true);
        base.OnPreviewMouseLeftButtonDown(e);
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        SetPressedVisual(false);
        base.OnPreviewMouseLeftButtonUp(e);
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        SetPressedVisual(false);
        base.OnMouseLeave(e);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        SetPressedVisual(false);
        base.OnLostMouseCapture(e);
    }
    internal void SetPressedVisual(bool pressed)
    {
        var scale = (ScaleTransform)_view.FindName("GridMain_ScaleTransform");
        var animation = new DoubleAnimation(pressed ? .85 : 1, TimeSpan.FromMilliseconds(MotionEnabled ? (pressed ? 60 : 230) : 0)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
    internal void ResetVisual()
    {
        if (_view is null)
            return;
        _favoriteAnimation?.Remove(this);
        _favoriteAnimation = null;
        foreach (var name in new[] { "BorderIcon", "BorderBackgroundCircle", "BorderIconFilled", "BorderIconFilledReversed" })
            ((Border)_view.FindName(name)).BeginAnimation(UIElement.OpacityProperty, null);
        ((Border)_view.FindName("BorderIcon")).Opacity = IsFavorited ? 0 : 1;
        ((Border)_view.FindName("BorderBackgroundCircle")).Opacity = IsFavorited ? 0 : .1;
        ((Border)_view.FindName("BorderIconFilled")).Opacity = 0;
        ((Border)_view.FindName("BorderIconFilledReversed")).Opacity = IsFavorited ? .5 : 0;
        foreach (var name in new[] { "BorderIcon_ScaleTransform", "BorderIconFilled_ScaleTransform", "BorderIconFilledReversed_ScaleTransform" })
        {
            var scale = (ScaleTransform)_view.FindName(name);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = 1;
        }
        var glow = (System.Windows.Media.Effects.DropShadowEffect)_view.FindName("BorderIconFilled_DropShadowEffect");
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
        glow.Opacity = .7;
    }
    private void StartFavoriteAnimation()
    {
        var story = new Storyboard();
        var grow = new ExponentialEase { Exponent = 5.2, EasingMode = EasingMode.EaseOut };
        var settle = new BackEase { Amplitude = 1, EasingMode = EasingMode.EaseOut };
        Add("BorderIcon", UIElement.OpacityProperty, (0, 1, null), (100, 1, null), (150, 0, null));
        Add("BorderBackgroundCircle", UIElement.OpacityProperty, (0, .1, null), (900, .1, null), (1050, 0, null));
        Add("BorderIconFilled", UIElement.OpacityProperty, (0, 0, null), (100, 0, null), (150, .6, null), (400, .85, null), (600, .85, null), (750, .6, null), (900, .6, null), (1100, 0, null));
        Add("BorderIconFilledReversed", UIElement.OpacityProperty, (0, 0, null), (900, 0, null), (1050, .5, null));
        Add("BorderIconFilled_DropShadowEffect", System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, (0, .7, null), (100, .7, null), (300, .75, null), (600, .75, null), (750, 0, null));
        double turn = 1 + .4 * grow.Ease(600d / 700);
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            Add("BorderIcon_ScaleTransform", property, (0, 1, null), (760, 1.4, grow));
            Add("BorderIconFilled_ScaleTransform", property, (0, 1, null), (600, turn, new CroppedGrowEase()), (980, 1, settle));
            Add("BorderIconFilledReversed_ScaleTransform", property, (0, 1, null), (600, 1, null), (600, turn, null), (980, 1, settle));
        }
        _favoriteAnimation = story;
        story.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        void Add(string name, DependencyProperty property, params (double Time, double Value, IEasingFunction? Ease)[] frames)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            foreach (var (time, value, ease) in frames)
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(time))) { EasingFunction = ease });
            DependencyObject target = (DependencyObject)_view.FindName(name);
            var path = new PropertyPath(property);
            // Target the owner so timeline cloning retains the displayed transform and effect.
            if (target is ScaleTransform)
            {
                target = (DependencyObject)_view.FindName(name.Replace("_ScaleTransform", ""));
                path = new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, property);
            }
            else if (target is System.Windows.Media.Effects.DropShadowEffect)
            {
                target = (DependencyObject)_view.FindName(name.Replace("_DropShadowEffect", ""));
                path = new PropertyPath("(0).(1)", UIElement.EffectProperty, property);
            }
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, path);
            story.Children.Add(animation);
        }
    }
    private sealed class CroppedGrowEase : EasingFunctionBase
    {
        public CroppedGrowEase()
        {
            EasingMode = EasingMode.EaseIn;
        }
        protected override double EaseInCore(double time)
        {
            var ease = new ExponentialEase { Exponent = 5.2, EasingMode = EasingMode.EaseOut };
            return ease.Ease(time * 600 / 700) / ease.Ease(600d / 700);
        }
        protected override Freezable CreateInstanceCore() => new CroppedGrowEase();
    }
}
public sealed class IconMenuItem : MenuItem
{
    private string _symbolIcon = "";
    public string SymbolIcon
    {
        get => _symbolIcon; set
        {
            _symbolIcon = value;
            Icon = string.IsNullOrWhiteSpace(value) || value == "Empty" ? null : new SymbolIcon { Symbol = value, FontSize = 16 };
        }
    }
}
public sealed class SymbolIcon : TextBlock
{
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(nameof(Symbol), typeof(string), typeof(SymbolIcon), new PropertyMetadata("", (d, e) => ((SymbolIcon)d).UpdateSymbol((string)e.NewValue)));
    public string Symbol
    {
        get => (string)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value);
    }
    public SymbolIcon()
    {
        FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#FluentSystemIcons-Regular");
        FontSize = 16;
    }
    private void UpdateSymbol(string value)
    {
        int code = value switch
        {
            "Dismiss16" => 62312,
            "Maximize16" => 62771,
            "SquareMultiple16" => 60309,
            "Subtract16" => 60367,
            "PaddingTop20" => 63156,
            "PaddingDown20" => 63105,
            "Location16" => 62711,
            "CircleOff16" => 58172,
            "Checkmark12" => 62099,
            "Library16" => 59233,
            _ => 0
        };
        Text = code == 0 ? "" : char.ConvertFromUtf32(code);
    }
}
public sealed class BreathingDots : UserControl, IDisposable
{
    private readonly UserControl _view = (UserControl)Application.LoadComponent(new Uri("/Cliric;component/Assets/ReferenceDots.xaml", UriKind.Relative));
    private readonly ScaleTransform _scale = new(1, 1, .5, .5);
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Cliric.Core.BreathingRun _run;
    private Cliric.Core.ViewOptions? _options;
    private Func<double>? _positionSource, _clockSource;
    private Func<bool>? _playingSource;
    private (double Start, double End)? _gap;
    private string? _track;
    private long _generation = long.MinValue;
    private int _startupVersion;
    private bool _disposed, _restart, _cacheEnabled;
    private double _samplePosition, _sampleClock;
    private bool _samplePlaying;
    public BreathingDots()
    {
        Content = _view;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        _run = new(() => _options?.AppleMusicLyrics is { BuggyApple: true } settings && settings.BuggyAppleOptions.TremendousBreathingDots && Random.Shared.NextDouble() < .5);
        _timer.Tick += (_, _) => SamplePlaybackTick(CurrentPosition(), CurrentPlaying(), CurrentClock());
        Unloaded += (_, _) => StopPlayback();
    }
    public Cliric.Core.BreathingFrame Frame { get; private set; } = new(false, 1, 0, .15, .15, .15);
    public bool PlaybackActive => _run.IsRunning || _run.IsExiting;
    public void ConfigurePlayback(Func<double> position, Func<bool> playing, Func<double> clock)
    {
        _positionSource = position;
        _playingSource = playing;
        _clockSource = clock;
    }
    public void SetAlignment(HorizontalAlignment? alignment, Cliric.Core.AppleMusicSettings settings)
    {
        if (settings.RightAlignFirst)
            alignment = alignment == HorizontalAlignment.Right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        if (settings.BreathingDotsAlignmentLeft)
            alignment = settings.RightAlignFirst ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        HorizontalAlignment = alignment.GetValueOrDefault();
        RenderTransformOrigin = new Point(HorizontalAlignment == HorizontalAlignment.Left ? .1 : .9, .5);
    }
    public void Advance(double position, double start, double end, bool delayed, bool reduceMotion)
    {
        Frame = Cliric.Core.BreathingMotion.At(position, start, end, delayed, reduceMotion);
        Render();
    }
    public void AdvancePlayback(double rawPosition, (double Start, double End)? gap, double now, bool playing, long generation, string track, Cliric.Core.ViewOptions options, bool allowStart = true, bool karaoke = true)
    {
        if (_disposed)
            return;
        _options = options;
        _samplePosition = rawPosition + options.TimelineOffsetMs;
        _sampleClock = now;
        _samplePlaying = playing;
        bool restart = _restart || (_track == track && _generation != long.MinValue && generation != _generation);
        if (_restart || _track != track || _generation != generation)
        {
            StopPlayback();
            _track = track;
            _generation = generation;
        }
        Frame = _run.FrameAt(now, options.ReduceMotion);
        if (allowStart && gap is { } candidate && candidate != _gap)
        {
            StopPlayback();
            if (candidate.Start > 0 || candidate.End - _samplePosition > 6000)
            {
                _gap = candidate;
                double end = candidate.End + (restart ? 0 : Cliric.Core.Motion.BreathingEndExtensionMs(options.AppleMusicLyrics, karaoke));
                _run.Start(candidate.Start > 0 ? candidate.Start : rawPosition, end, candidate.Start > 0 && !restart, now);
                if (_run.IsRunning)
                    _ = StartPlaybackAsync(++_startupVersion, candidate.Start > 0 && !restart);
            }
        }
        else if (gap is null && _run.Pending)
            StopPlayback();
        SetCache(options.AppleMusicLyrics.EnablePlusLighter);
        Frame = _run.FrameAt(now, options.ReduceMotion);
        Render();
    }
    private async Task StartPlaybackAsync(int version, bool delayed)
    {
        // shortcut: obsolete startup callbacks are coalesced by flow token; validate rapid restart against native scheduling.
        await Task.Delay(500);
        if (_disposed || version != _startupVersion)
            return;
        SamplePlaybackTick(CurrentPosition(), CurrentPlaying(), CurrentClock());
        if (delayed)
        {
            await Task.Delay(500);
            if (_disposed || version != _startupVersion)
                return;
            SamplePlaybackTick(CurrentPosition(), CurrentPlaying(), CurrentClock());
        }
        if (_run.IsRunning && !_run.Pending && _positionSource is not null)
            _timer.Start();
    }
    internal void SamplePlaybackTick(double position, bool playing, double now)
    {
        if (_disposed)
            return;
        _samplePosition = position;
        _samplePlaying = playing;
        _sampleClock = now;
        _run.Tick(position, playing, now);
        Frame = _run.FrameAt(now, _options?.ReduceMotion == true);
        Render();
        if (!_run.IsRunning)
            _timer.Stop();
    }
    public void RestartForLayout()
    {
        if (_run.IsRunning)
            _restart = true;
    }
    public void SuspendForManualScroll()
    {
        if (_run.IsRunning)
            StopPlayback();
    }
    internal double PlaybackEndMs => _run.EndMs;
    public void StopPlayback()
    {
        _startupVersion++;
        _timer.Stop();
        _run.Stop();
        _gap = null;
        _restart = false;
        Frame = _run.FrameAt(_sampleClock, _options?.ReduceMotion == true);
        Render();
    }
    private double CurrentPosition() => _positionSource?.Invoke() ?? _samplePosition;
    private double CurrentClock() => _clockSource?.Invoke() ?? _sampleClock;
    private bool CurrentPlaying() => _playingSource?.Invoke() ?? _samplePlaying;
    private void SetCache(bool enabled)
    {
        if (_cacheEnabled == enabled)
            return;
        _cacheEnabled = enabled;
        foreach (var element in new FrameworkElement[] { this, (Viewbox)_view.FindName("Viewbox"), (Grid)_view.FindName("GridMain"), (TextBlock)_view.FindName("TextBlock1"), (TextBlock)_view.FindName("TextBlock2"), (TextBlock)_view.FindName("TextBlock3") })
            element.CacheMode = enabled ? new BitmapCache { RenderAtScale = 3 } : null;
    }
    private void Render()
    {
        Visibility = Frame.Visible ? Visibility.Visible : Visibility.Collapsed;
        var grid = (Grid)_view.FindName("GridMain");
        grid.RenderTransformOrigin = new Point(.5, .5);
        grid.RenderTransform = _scale;
        _scale.ScaleX = _scale.ScaleY = Frame.Scale;
        grid.Opacity = Frame.Opacity;
        ((TextBlock)_view.FindName("TextBlock1")).Opacity = Frame.First;
        ((TextBlock)_view.FindName("TextBlock2")).Opacity = Frame.Second;
        ((TextBlock)_view.FindName("TextBlock3")).Opacity = Frame.Third;
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopPlayback();
    }
}
public sealed class VisibilityConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => value is Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => value is Visibility.Visible;
}
public static class RoundedClip
{
    public static readonly DependencyProperty CornerSmoothingProperty = DependencyProperty.RegisterAttached("CornerSmoothing", typeof(double), typeof(RoundedClip), new PropertyMetadata(0d, (d, _) => { if (d is FrameworkElement element) Apply(element); }));
    public static void SetCornerSmoothing(DependencyObject d, double value) => d.SetValue(CornerSmoothingProperty, value);
    public static double GetCornerSmoothing(DependencyObject d) => (double)d.GetValue(CornerSmoothingProperty);
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached("CornerRadius", typeof(double), typeof(RoundedClip), new PropertyMetadata(0d, (d, e) => { if (d is FrameworkElement element) { element.SizeChanged -= OnSize; element.SizeChanged += OnSize; Apply(element); } }));
    public static void SetCornerRadius(DependencyObject d, double value) => d.SetValue(CornerRadiusProperty, value);
    public static double GetCornerRadius(DependencyObject d) => (double)d.GetValue(CornerRadiusProperty);
    private static void OnSize(object sender, SizeChangedEventArgs e) => Apply((FrameworkElement)sender);
    private static void Apply(FrameworkElement element) => element.Clip = SquircleGeometry.Create(element.ActualWidth, element.ActualHeight, GetCornerRadius(element), GetCornerSmoothing(element));
}
public sealed class InfoMarquee : ContentControl
{
    private static readonly List<WeakReference<InfoMarquee>> Instances = [];
    public string SyncGroup { get; set; } = "";
    public TimeSpan AnimationDelay { get; set; } = TimeSpan.FromSeconds(2);
    private ContentPresenter? _presenter;
    private Grid? _host;
    private Canvas? _items;
    private System.Windows.Shapes.Rectangle? _copy;
    private bool _syncPending;
    public bool MotionEnabled { get; set; } = true;
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _presenter = GetTemplateChild("PART_Content") as ContentPresenter;
        _host = GetTemplateChild("PART_Grid") as Grid;
        _copy = GetTemplateChild("PART_ContentCopy") as System.Windows.Shapes.Rectangle;
        _items = GetTemplateChild("PART_ItemsCanvas") as Canvas;
        if (_host is not null)
            _host.Margin = new Thickness(-16, 0, -16, 0);
        Dispatcher.InvokeAsync(Recalculate, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    public InfoMarquee()
    {
        Instances.Add(new(this));
        SizeChanged += (_, _) => Recalculate();
        Unloaded += (_, _) => _items?.BeginAnimation(Canvas.LeftProperty, null);
    }
    protected override Size MeasureOverride(Size constraint)
    {
        ApplyTemplate();
        if (_presenter is null)
            return base.MeasureOverride(constraint);
        _presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _presenter.DesiredSize;
        if (_host is not null)
            _host.Height = size.Height;
        base.MeasureOverride(new Size(constraint.Width, size.Height));
        return new Size(double.IsPositiveInfinity(constraint.Width) ? size.Width : Math.Min(constraint.Width, size.Width), size.Height);
    }
    public void Recalculate()
    {
        if (_presenter is null || _host is null || _copy is null || _items is null)
            return;
        _items.BeginAnimation(Canvas.LeftProperty, null);
        Canvas.SetLeft(_items, 16);
        Canvas.SetLeft(_presenter, 0);
        _presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = _presenter.DesiredSize.Width;
        if (GetTemplateChild("PART_StopStart") is GradientStop start)
            start.Offset = 16 / (ActualWidth + 32);
        if (GetTemplateChild("PART_StopEnd") is GradientStop end)
            end.Offset = 1 - 16 / (ActualWidth + 32);
        _copy.Visibility = Visibility.Collapsed;
        if (!MotionEnabled || width <= ActualWidth || ActualWidth <= 0)
        {
            double left = (Content as FrameworkElement)?.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => (ActualWidth - width) / 2 + 16,
                HorizontalAlignment.Right => ActualWidth - width + 16,
                _ => 16
            };
            Canvas.SetLeft(_items, left);
            return;
        }
        if (_syncPending)
            return;
        _syncPending = true;
        Dispatcher.InvokeAsync(() => { _syncPending = false; Synchronize(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void Synchronize()
    {
        Instances.RemoveAll(x => !x.TryGetTarget(out _));
        var group = Instances.Select(x => x.TryGetTarget(out var target) ? target : null).OfType<InfoMarquee>()
            .Where(x => (ReferenceEquals(x, this) || (!string.IsNullOrWhiteSpace(SyncGroup) && x.SyncGroup == SyncGroup)) && x.MotionEnabled && x.ActualWidth > 0 && x._presenter?.DesiredSize.Width > x.ActualWidth).ToList();
        if (group.Count == 0)
            return;
        double delay = group.Max(x => Math.Max(0, x.AnimationDelay.TotalMilliseconds));
        var plans = group.Select(x => (View: x, Timing: Cliric.Core.MarqueeTiming.Create(x._presenter!.DesiredSize.Width, delay))).ToList();
        double duration = plans.Max(x => x.Timing.End);
        var story = new Storyboard { Duration = TimeSpan.FromMilliseconds(duration), RepeatBehavior = RepeatBehavior.Forever };
        foreach (var (view, timing) in plans)
        {
            if (view._items is null || view._presenter is null || view._copy is null)
                continue;
            var rect = new Rect(0, 0, timing.Width, view._presenter.DesiredSize.Height);
            view._copy.Width = rect.Width;
            view._copy.Height = rect.Height;
            view._copy.Visibility = Visibility.Visible;
            view._copy.Fill = new VisualBrush(view._presenter) { Stretch = Stretch.Fill, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, ViewportUnits = BrushMappingMode.Absolute, Viewbox = rect, Viewport = rect };
            Canvas.SetLeft(view._copy, timing.Width + 45);
            Canvas.SetLeft(view._items, timing.From);
            var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(duration), FillBehavior = FillBehavior.HoldEnd };
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(timing.From, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(timing.From, At(timing.Delay)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(timing.EnterPosition, At(timing.EnterEnd)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(timing.CruisePosition, At(timing.CruiseEnd)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(timing.To, At(timing.End)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            if (duration > timing.End + .5)
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(timing.To, At(duration)));
            Storyboard.SetTarget(animation, view._items);
            Storyboard.SetTargetProperty(animation, new PropertyPath(Canvas.LeftProperty));
            story.Children.Add(animation);
        }
        story.Begin(this, HandoffBehavior.SnapshotAndReplace, false);
        static KeyTime At(double value) => KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(value));
    }
}
