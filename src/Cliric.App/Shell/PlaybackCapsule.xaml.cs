using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using LiquidGlass;
using LiquidGlass.Wpf;

namespace Cliric.App;

public sealed partial class PlaybackCapsule : UserControl
{
    internal const double CompactWidth = 384, ExpandedWidth = 760, CompactHeight = 54;
    internal const double MinimumSeekWidth = 320, ToolGap = 10;
    private const double TrackTextSpace = 144, WideToolsWidth = 156, NarrowToolsWidth = 84;
    internal const double ShapeTimeScale = 1.2;
    // Stretch the M3 springs in time while retaining their damping ratio.
    private static readonly SpringOptions SpatialSpring = new(ShapeTimeScale * Math.Tau / Math.Sqrt(700), 1 - .9);
    private static readonly SpringOptions EffectsSpring = new(ShapeTimeScale * Math.Tau / Math.Sqrt(3800), 0);
    private static readonly IEasingFunction TintEase = new CubicEase { EasingMode = EasingMode.EaseOut };
    // shortcut: The pinned renderer exposes geometry-only updates internally, replace with a public API when available.
    private static readonly Action<GlassLens, Rect> UpdateGlassConstants = typeof(GlassLens)
        .GetMethod("UpdateShaderConstants", BindingFlags.Instance | BindingFlags.NonPublic)!
        .CreateDelegate<Action<GlassLens, Rect>>();
    private readonly Spring _shape = new(0, SpatialSpring), _details = new(0, EffectsSpring);
    private readonly TranslateTransform _trackShift = new(), _transportShift = new();
    private readonly DrawingBrush _fallbackMask;
    private Rect _capsuleBounds = Rect.Empty;
    private bool _seekFits;
    private double _compactTitleWidth, _expandedTitleWidth;
    private bool _shapeRendering;
    private TimeSpan? _lastShapeFrame;

    internal Rect CapsuleBounds => _capsuleBounds;
    internal bool Expanded => ShapeButton.IsChecked == true;
    internal bool ShapeRendering => _shapeRendering;
    internal double ExpansionVelocity => _shape.Velocity;
    internal double DetailVelocity => _details.Velocity;
    internal bool HasSoundTool => SoundTool.Visibility == Visibility.Visible;
    internal event Action? LayoutChanged;

    public PlaybackCapsule()
    {
        InitializeComponent();
        _fallbackMask = new DrawingBrush(new GeometryDrawing(Brushes.White, null, CapsuleOutline)) { Stretch = Stretch.None };
        ToolShade.Background = GlassColor;
        TrackStamp.RenderTransform = _trackShift;
        Transport.RenderTransform = ShapeButton.RenderTransform = _transportShift;
        SizeChanged += (_, _) => ArrangeCapsule();
        ToolGroup.SizeChanged += (_, e) => ToolOutline.Rect = new Rect(e.NewSize);
        Loaded += (_, _) =>
        {
            CapsuleGlass.LayoutUpdated += OnLensLayoutUpdated;
            ArrangeCapsule();
            AnimateShape(immediate: true);
        };
        Unloaded += (_, _) =>
        {
            CapsuleGlass.LayoutUpdated -= OnLensLayoutUpdated;
            StopShapeFrames();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible && _shapeRendering)
                AnimateShape(immediate: true);
        };
    }

    internal void InitializeControls()
    {
        ShellPlaybackControls.Initialize(PreviousButton, PlayButton, NextButton);
        Seek.UseShellLayout();
        ApplyPalette(playing: false);
        ArrangeCapsule();
    }

    internal void ApplyPalette(bool playing)
    {
        bool contrast = SystemParameters.HighContrast;
        var text = contrast ? SystemColors.ControlTextBrush : (Brush)FindResource("Shell.Primary");
        Resources["CapsuleControlText"] = text;
        Resources["CapsuleCaption"] = contrast ? text : new SolidColorBrush(Color.FromRgb(225, 229, 240));
        Resources["CapsuleFocusBrush"] = contrast ? SystemColors.HighlightBrush : (Brush)FindResource("Shell.Accent");
        GlassShade.Visibility = ToolShade.Visibility = contrast ? Visibility.Collapsed : Visibility.Visible;
        ShellPlaybackControls.ApplyPalette(PreviousButton, PlayButton, NextButton, playing);
        Seek.ApplyShellPalette();
    }

    internal void ApplyMotionPreference(bool reduced)
    {
        Seek.SetShellReducedMotion(reduced);
        if (reduced)
            AnimateShape(immediate: true);
    }

    internal Color SetArtwork(ImageSource? artwork)
    {
        MiniCover.ImageSource = artwork;
        var tint = artwork is BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } bitmap
            ? SampleTint(bitmap) : Colors.Transparent;
        Color previous = GlassColor.Color;
        GlassColor.BeginAnimation(SolidColorBrush.ColorProperty, null);
        GlassColor.Color = tint;
        if (!ShellTransitions.GetReducedMotion(this))
            GlassColor.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(previous, tint, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = TintEase,
                FillBehavior = FillBehavior.Stop
            });
        return tint;
    }

    private static Color SampleTint(BitmapSource cover)
    {
        var thumbnail = new TransformedBitmap(cover, new ScaleTransform(16.0 / cover.PixelWidth, 16.0 / cover.PixelHeight));
        var sample = new FormatConvertedBitmap(thumbnail, PixelFormats.Bgra32, null, 0);
        int stride = sample.PixelWidth * 4;
        var pixels = new byte[stride * sample.PixelHeight];
        sample.CopyPixels(pixels, stride, 0);
        int r = 0, g = 0, b = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            b += pixels[i];
            g += pixels[i + 1];
            r += pixels[i + 2];
        }
        int count = pixels.Length / 4;
        return Color.FromArgb(30, (byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    private void OnLensLayoutUpdated(object? sender, EventArgs e) => ApplyGlassOptics();

    private void OnShapeChanged(object sender, RoutedEventArgs e)
    {
        ShapeButton.ToolTip = Expanded ? "收起播放栏" : "展开播放栏";
        AnimateShape();
    }

    internal void AnimateShape(bool immediate = false)
    {
        bool expanded = Expanded;
        if (!expanded)
        {
            Seek.CancelPreview();
            if (Seek.IsKeyboardFocusWithin || PreviousButton.IsKeyboardFocusWithin)
                ShapeButton.Focus();
        }

        PreviousSlot.IsHitTestVisible = PreviousButton.IsTabStop = expanded;
        double target = expanded ? 1 : 0;
        if (immediate || ShellTransitions.GetReducedMotion(this) || !IsVisible)
        {
            _shape.Jump(target);
            _details.Jump(target);
            StopShapeFrames();
        }
        else
        {
            _shape.Animate(target, SpatialSpring);
            _details.Animate(target, EffectsSpring);
            if (!_shapeRendering && (!_shape.IsSettled || !_details.IsSettled))
            {
                _shapeRendering = true;
                CompositionTarget.Rendering += OnShapeFrame;
            }
        }
        PresentCapsule();
    }

    private void OnShapeFrame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs frame || frame.RenderingTime == _lastShapeFrame)
            return;
        double seconds = _lastShapeFrame is { } last ? (frame.RenderingTime - last).TotalSeconds : 1d / 60;
        _lastShapeFrame = frame.RenderingTime;
        if (seconds <= 0)
            return;
        _shape.Step(seconds);
        _details.Step(seconds);
        // Skip the invisible tail once a frame of movement fits within the display tolerance.
        SnapAtDisplayRest(_shape, .5 / (ExpandedWidth - CompactWidth), seconds);
        SnapAtDisplayRest(_details, 1d / 255, seconds);
        if (_shape.IsSettled && _details.IsSettled)
            StopShapeFrames();
        PresentCapsule();
    }

    private static void SnapAtDisplayRest(Spring spring, double tolerance, double frameSeconds)
    {
        if (!spring.IsSettled && Math.Abs(spring.Value - spring.Target) + Math.Abs(spring.Velocity * frameSeconds) <= tolerance)
            spring.Jump(spring.Target);
    }

    private void StopShapeFrames()
    {
        if (_shapeRendering)
        {
            CompositionTarget.Rendering -= OnShapeFrame;
            _shapeRendering = false;
            _lastShapeFrame = null;
        }
    }

    private void ArrangeCapsule()
    {
        double available = ActualWidth - Capsule.Margin.Left - Capsule.Margin.Right;
        if (available <= 0)
            return;

        bool wideTools = available >= CompactWidth + 2 * (WideToolsWidth + ToolGap);
        ToolGroup.Width = wideTools ? WideToolsWidth : NarrowToolsWidth;
        SoundTool.Visibility = wideTools ? Visibility.Visible : Visibility.Collapsed;
        QueueTool.Width = LyricsTool.Width = wideTools ? 48 : 36;
        double expandedWidth = Math.Clamp(available - 2 * (ToolGroup.Width + ToolGap), 1, ExpandedWidth);
        double buttons = PreviousButton.Width + PlayButton.Width + PlayButton.Margin.Left + PlayButton.Margin.Right +
            NextButton.Width + Transport.Margin.Left + Transport.Margin.Right + ShapeButton.Width;
        if (!double.IsFinite(buttons))
            return;

        Capsule.Width = expandedWidth;
        PreviousSlot.Width = PreviousButton.Width;
        double fixedWidth = buttons + CapsuleContent.Margin.Left + CapsuleContent.Margin.Right + TrackStamp.Margin.Right;
        double seekMargins = Seek.Margin.Left + Seek.Margin.Right;
        double seekWidth = Math.Min(336, expandedWidth - fixedWidth - TrackTextSpace - seekMargins);
        _seekFits = seekWidth >= MinimumSeekWidth;
        Seek.Width = Math.Max(MinimumSeekWidth, seekWidth);
        SeekSlot.Width = _seekFits ? seekWidth + seekMargins : 0;
        SeekSlot.Height = Seek.Height;
        _compactTitleWidth = Math.Max(42, Math.Min(expandedWidth, CompactWidth) - fixedWidth + PreviousButton.Width);
        _expandedTitleWidth = Math.Max(42, expandedWidth - fixedWidth - SeekSlot.Width);
        PresentCapsule();
        LayoutChanged?.Invoke();
    }

    private void PresentCapsule()
    {
        double progress = Math.Clamp(_shape.Value, 0, 1);
        double inset = Math.Max(0, Capsule.Width - CompactWidth) * (1 - progress) / 2;
        var bounds = new Rect(inset, 0, Capsule.Width - 2 * inset, CompactHeight);
        if (_capsuleBounds != bounds)
        {
            _capsuleBounds = bounds;
            CapsuleOutline.Rect = bounds;
            ApplyGlassOptics();
            ShadeBounds.Rect = new Rect(bounds.Left + 1, bounds.Top + 1, Math.Max(0, bounds.Width - 2), bounds.Height - 2);
            ShadeBounds.RadiusX = ShadeBounds.RadiusY = bounds.Height / 2 - 1;
            _trackShift.X = inset;
            _transportShift.X = -inset;
        }
        double titleWidth = _shapeRendering ? Math.Min(_expandedTitleWidth, _compactTitleWidth - PreviousButton.Width)
            : Expanded ? _expandedTitleWidth : _compactTitleWidth;
        TrackStamp.MaxWidth = Math.Max(42, titleWidth);
        double coverWidth = CoverFrame.Width + CoverFrame.Margin.Right;
        CoverFrame.Visibility = TrackStamp.MaxWidth - coverWidth >= 62 ? Visibility.Visible : Visibility.Collapsed;
        ShapeArrow.Angle = 180 * progress;
        double trim = Math.Min(inset, SeekSlot.Width / 2);
        SeekClip.Rect = new Rect(trim, 0, Math.Max(0, SeekSlot.Width - 2 * trim), Seek.Height);
        SeekSlot.Opacity = PreviousSlot.Opacity = Math.Clamp(_details.Value, 0, 1);
        bool controlsVisible = Expanded || _shapeRendering;
        PreviousSlot.Visibility = controlsVisible ? Visibility.Visible : Visibility.Hidden;
        bool seekVisible = _seekFits && controlsVisible;
        if (!seekVisible && SeekSlot.Visibility == Visibility.Visible)
        {
            Seek.CancelPreview();
            if (Seek.IsKeyboardFocusWithin)
                ShapeButton.Focus();
        }
        SeekSlot.Visibility = seekVisible ? Visibility.Visible : Visibility.Hidden;
        SeekSlot.IsHitTestVisible = Seek.IsTabStop = seekVisible && Expanded && Seek.Width - 2 * trim >= MinimumSeekWidth;
    }

    private void ApplyGlassOptics()
    {
        bool usesShader = CapsuleGlass.Mode == GlassLensMode.Shader;
        // The shader draws its own fractional edge coverage and rim.
        var mask = usesShader ? null : _fallbackMask;
        if (CapsuleGlass.OpacityMask != mask)
            CapsuleGlass.OpacityMask = mask;
        if (usesShader && !_capsuleBounds.IsEmpty)
            UpdateGlassConstants(CapsuleGlass, _capsuleBounds);
    }
}
