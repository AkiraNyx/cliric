using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiquidGlass;
using LiquidGlass.Wpf;
using LyricReplica.App;
using Timeline = LyricReplica.Core.Timeline;

namespace CapsulePlayerDemo;

public partial class MainWindow : Window
{
    private const double CompactWidth = 384, ExpandedWidth = 704, CompactHeight = 54;
    private const double MinimumSeekWidth = 320, TrackTextSpace = 144;
    private sealed record DemoTrack(string Title, string Artist, string Duration, BitmapSource Cover, Uri Audio, double DurationMs, string Lyrics);

    private readonly MediaPlayer _audio = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DemoTrack[] _tracks;
    private bool _playing, _ready;
    private bool Expanded => ShapeButton.IsChecked == true;
    private bool? _displayedPlaying;
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    private const double ShapeTimeScale = 1.2;
    // Stretch the M3 springs in time while retaining their damping ratio.
    private static readonly SpringOptions SpatialSpring = new(ShapeTimeScale * Math.Tau / Math.Sqrt(700), 1 - .9);
    private static readonly SpringOptions EffectsSpring = new(ShapeTimeScale * Math.Tau / Math.Sqrt(3800), 0);
    private readonly Spring _shape = new(0, SpatialSpring), _details = new(0, EffectsSpring);
    // shortcut: The pinned renderer exposes geometry-only updates internally, replace with a public API when available.
    private static readonly Action<GlassLens, Rect> UpdateGlassConstants = typeof(GlassLens)
        .GetMethod("UpdateShaderConstants", BindingFlags.Instance | BindingFlags.NonPublic)!
        .CreateDelegate<Action<GlassLens, Rect>>();
    private readonly TranslateTransform _trackShift = new(), _transportShift = new();
    private Rect _capsuleBounds = Rect.Empty;
    private bool _seekFits;
    private bool _shapeRendering;
    private TimeSpan? _lastShapeFrame;

    public MainWindow()
    {
        InitializeComponent();
        TrackStamp.RenderTransform = _trackShift;
        Transport.RenderTransform = ShapeButton.RenderTransform = _transportShift;
        FontFamily = new FontFamily(new Uri("pack://application:,,,/LyricReplica;component/"), "./Assets/Fonts/#MiSans VF, Microsoft YaHei UI");
        ShellPlaybackControls.Initialize(PreviousButton, PlayButton, NextButton);
        Seek.UseShellLayout();
        Seek.IsRealTimeUpdate = true;
        Seek.SeekRequested += OnSeek;
        ApplyCapsulePalette();
        ApplyReducedMotion(!SystemParameters.ClientAreaAnimation);
        SystemParameters.StaticPropertyChanged += OnSystemPreferencesChanged;
        string assetDirectory = Path.Combine(AppContext.BaseDirectory, "DemoMedia");
        var info = new[]
        {
            ("沿光而行", "Lyric Lab", 36000d, "雨还在，敲着海岸\n天亮以前，沿光而行\n总会看见新的天色"),
            ("回声之间", "Voice A / Voice B", 32000d, "让声音穿过空房间\n在回声之间\n我听见你慢慢走远"),
            ("Slow Motion", "Lyric Lab", 44000d, "The streetlights drift away\nWe take the long way home\nLet the morning come in slow")
        };
        _tracks = info.Select((item, index) => new DemoTrack(item.Item1, item.Item2, Timeline.Format(item.Item3),
            Artwork.Create(index), new Uri(Path.Combine(assetDirectory, $"track-{index + 1}.wav")), item.Item3, item.Item4)).ToArray();
        _audio.Volume = DemoVolume.Value / 100;
        _audio.MediaOpened += (_, _) =>
        {
            _ready = true;
            PlayButton.IsEnabled = Seek.IsEnabled = true;
            if (_playing)
                _audio.Play();
        };
        _audio.MediaEnded += (_, _) => SelectTrack(1, atEnd: true);
        _audio.MediaFailed += (_, _) =>
        {
            _ready = false;
            PlayButton.IsEnabled = Seek.IsEnabled = false;
            _playing = false;
            UpdatePlayIcon();
        };
        _clock.Tick += (_, _) => RefreshClock();
        QueueTracks.ItemsSource = Tracks.ItemsSource = _tracks;
        Tracks.SelectedIndex = 0;
        UpdatePlayIcon();
        Root.SizeChanged += (_, _) => ArrangeCapsule();
        ToolGroup.SizeChanged += (_, e) => ToolOutline.Rect = new Rect(e.NewSize);
        CapsuleGlass.LayoutUpdated += (_, _) => ApplyGlassOptics();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                AnimateShape(immediate: true);
        };
        Loaded += (_, _) =>
        {
            ArrangeCapsule();
            AnimateShape(immediate: true);
            if (Environment.GetCommandLineArgs().Contains("--verify"))
                Dispatcher.InvokeAsync(VerifyPreview, DispatcherPriority.ApplicationIdle);
            else
                _clock.Start();
        };
        Closed += (_, _) =>
        {
            StopShapeFrames();
            _clock.Stop();
            _audio.Close();
            SystemParameters.StaticPropertyChanged -= OnSystemPreferencesChanged;
        };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += (_, _) =>
        {
            if (_toolAnchor is not null && !ToolFlyout.IsMouseOver && !ToolGroup.IsMouseOver && !TrackStamp.IsMouseOver)
                CloseToolFlyout();
        };
    }

    private void OnTrackSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Tracks.SelectedItem is not DemoTrack track)
            return;
        _ready = false;
        PlayButton.IsEnabled = Seek.IsEnabled = false;
        _audio.Open(track.Audio);
        HeroTitle.Text = CapsuleTitle.Text = track.Title;
        HeroArtist.Text = CapsuleArtist.Text = track.Artist;
        TrackStamp.ToolTip = $"{track.Title}\n{track.Artist}";
        QueueTracks.SelectedItem = track;
        TrackOptionTitle.Text = LyricTitle.Text = track.Title;
        DemoLyrics.Text = track.Lyrics;
        UpdateFavorite();
        CurrentCover.ImageSource = MiniCover.ImageSource = track.Cover;
        ApplyCoverTint(track.Cover);
        Seek.CancelPreview();
        Seek.SetPosition(0, track.DurationMs);
    }

    private void ApplyCoverTint(BitmapSource cover)
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
        AnimateColor(GlassColor, SolidColorBrush.ColorProperty,
            Color.FromArgb(30, (byte)(r / count), (byte)(g / count), (byte)(b / count)));
        AnimateColor(SceneColor, GradientStop.ColorProperty, Color.FromRgb((byte)(r * .43 / count), (byte)(g * .43 / count), (byte)(b * .43 / count)));
    }

    private static void AnimateColor(Animatable brush, DependencyProperty colorProperty, Color target)
    {
        Color from = (Color)brush.GetValue(colorProperty);
        brush.BeginAnimation(colorProperty, null);
        brush.SetValue(colorProperty, target);
        if (SystemParameters.ClientAreaAnimation)
            brush.BeginAnimation(colorProperty, new ColorAnimation(from, target, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = Ease,
                FillBehavior = FillBehavior.Stop
            });
    }

    private void RefreshClock()
    {
        if (_ready)
            Seek.SetPosition(_audio.Position.TotalMilliseconds, _tracks[Tracks.SelectedIndex].DurationMs);
    }

    private void OnSeek(double position)
    {
        if (_ready && LyricReplica.App.MainWindow.PlayerSeekTarget(position, Seek.TotalTime, Seek.Dragging) is { } target)
            _audio.Position = TimeSpan.FromMilliseconds(target);
    }

    private void TogglePlay(object sender, RoutedEventArgs e) => TogglePlayback();

    private void TogglePlayback()
    {
        if (!_ready)
            return;
        _playing = !_playing;
        if (_playing)
            _audio.Play();
        else
            _audio.Pause();
        UpdatePlayIcon();
    }

    private void UpdatePlayIcon()
    {
        ShellPlaybackControls.SetPlaying(PlayButton, _playing, _displayedPlaying is { } previous && previous != _playing,
            ShellTransitions.GetReducedMotion(this));
        _displayedPlaying = _playing;
    }

    private void Previous(object sender, RoutedEventArgs e) => SelectTrack(-1);
    private void Next(object sender, RoutedEventArgs e) => SelectTrack(1);
    private void SelectTrack(int offset, bool atEnd = false)
    {
        if (QueueTracks.ItemsSource is not DemoTrack[] order || Tracks.SelectedItem is not DemoTrack track)
            return;
        int next = Array.IndexOf(order, track) + offset;
        if (atEnd && _repeatMode == DemoRepeat.Track)
        {
            _audio.Position = TimeSpan.Zero;
            _audio.Play();
        }
        else if (atEnd && next >= order.Length && _repeatMode == DemoRepeat.Off)
        {
            _playing = false;
            _audio.Position = TimeSpan.Zero;
            UpdatePlayIcon();
        }
        else
            Tracks.SelectedItem = order[(next + order.Length) % order.Length];
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Seek.Dragging)
            Seek.CancelPreview();
        else if (e.Key == Key.Escape && _toolAnchor is { } anchor)
        {
            CloseToolFlyout();
            anchor.Focus();
        }
        else if (e.Key == Key.Escape && Expanded)
            ShapeButton.IsChecked = false;
        else if (e.Key == Key.Space && !e.IsRepeat && e.KeyboardDevice.Modifiers == ModifierKeys.None &&
                 e.OriginalSource is not ButtonBase and not TextBoxBase)
            TogglePlayback();
        else
            return;
        e.Handled = true;
    }

    private void OnShapeChanged(object sender, RoutedEventArgs e)
    {
        ShapeButton.ToolTip = Expanded ? "收起播放栏" : "展开播放栏";
        AnimateShape();
    }

    private void AnimateShape(bool immediate = false)
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
        if (immediate || ShellTransitions.GetReducedMotion(this) || WindowState == WindowState.Minimized)
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
        double available = Root.ActualWidth - Capsule.Margin.Left - Capsule.Margin.Right;
        if (available <= 0)
            return;

        double expandedWidth = Math.Clamp(available - 2 * (ArrangeTools(available) + ToolGap), 1, ExpandedWidth);
        double buttons = PreviousButton.Width + PlayButton.Width + PlayButton.Margin.Left + PlayButton.Margin.Right +
            NextButton.Width + Transport.Margin.Left + Transport.Margin.Right + ShapeButton.Width;
        if (!double.IsFinite(buttons))
            return;

        Capsule.Width = expandedWidth;
        PreviousSlot.Width = PreviousButton.Width;
        double fixedWidth = buttons + CapsuleContent.Margin.Left + CapsuleContent.Margin.Right + TrackStamp.Margin.Right;
        double seekMargins = Seek.Margin.Left + Seek.Margin.Right;
        double seekWidth = expandedWidth - fixedWidth - TrackTextSpace - seekMargins;
        _seekFits = seekWidth >= MinimumSeekWidth;
        Seek.Width = Math.Max(MinimumSeekWidth, seekWidth);
        SeekSlot.Width = _seekFits ? seekWidth + seekMargins : 0;
        SeekSlot.Height = Seek.Height;
        TrackStamp.MaxWidth = Math.Max(42, Math.Min(Math.Min(expandedWidth, CompactWidth) - fixedWidth,
            expandedWidth - fixedWidth - SeekSlot.Width));
        PresentCapsule();
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
        if (CapsuleGlass.Mode == GlassLensMode.Shader && !_capsuleBounds.IsEmpty)
            UpdateGlassConstants(CapsuleGlass, _capsuleBounds);
    }

    private void OnSystemPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast)))
            return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => OnSystemPreferencesChanged(sender, e));
            return;
        }
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            ApplyCapsulePalette();
        else
            ApplyReducedMotion(!SystemParameters.ClientAreaAnimation);
    }

    private void ApplyCapsulePalette()
    {
        bool contrast = SystemParameters.HighContrast;
        Brush text = contrast ? SystemColors.ControlTextBrush : (Brush)FindResource("Shell.Primary");
        Resources["CapsuleControlText"] = text;
        Resources["CapsuleCaption"] = contrast ? text : new SolidColorBrush(Color.FromRgb(225, 229, 240));
        Resources["CapsuleFocusBrush"] = contrast ? SystemColors.HighlightBrush : (Brush)FindResource("Shell.Accent");
        Resources["CapsulePanelBrush"] = contrast ? SystemColors.WindowBrush : (Brush)FindResource("Shell.Surface");
        GlassShade.Visibility = ToolShade.Visibility = contrast ? Visibility.Collapsed : Visibility.Visible;
        ShellPlaybackControls.ApplyPalette(PreviousButton, PlayButton, NextButton, _playing);
        Seek.ApplyShellPalette();
    }

    private void ApplyReducedMotion(bool reduced)
    {
        ShellTransitions.SetReducedMotion(this, reduced);
        Seek.SetShellReducedMotion(reduced);
        if (reduced)
        {
            AnimateShape(immediate: true);
            Scene.FinishWheelAnimation();
            ShellTransitions.Settle(Dispatcher);
        }
    }

    private async void VerifyPreview()
    {
        try
        {
            _audio.Volume = 0;
            await Task.Delay(420);
            UpdateLayout();
            var mode = CapsuleGlass.Mode;
            if (ToolGlass.Mode != mode || ToolGlass.Refraction != CapsuleGlass.Refraction ||
                GlassScene.GetMaterial(ToolGlass) != GlassScene.GetMaterial(CapsuleGlass))
                throw new InvalidOperationException("Both capsules must use the same glass material and rendering path.");
            if (mode == GlassLensMode.Shader)
            {
                var wider = _capsuleBounds;
                wider.Inflate(2, 0);
                UpdateGlassConstants(CapsuleGlass, wider);
                if (!CapsuleGlass.IsMeasureValid || !CapsuleGlass.IsArrangeValid)
                    throw new InvalidOperationException("Updating the glass geometry must leave the lens layout valid.");
                ApplyGlassOptics();
            }
            if (new ToggleButtonAutomationPeer(ShapeButton).GetPattern(PatternInterface.Toggle) is not IToggleProvider toggle ||
                toggle.ToggleState != ToggleState.Off)
                throw new InvalidOperationException("The playback disclosure must expose its collapsed state to UI Automation.");
            var first = await CaptureAsync("capsule-preview.png");
            if (Math.Abs(_capsuleBounds.Width - CompactWidth) > .7 || Math.Abs(_capsuleBounds.Height - CompactHeight) > .7 ||
                Seek.IsVisible || Seek.IsTabStop || PreviousButton.IsTabStop || PlayButton.Content is not Image { Source: not null } ||
                !ReferenceEquals(PlayButton.Style, FindResource("Shell.IconButton")))
                throw new InvalidOperationException("The compact bar must use the regular playback controls at 384 × 54 DIP.");
            var original = GlassColor.Color;
            var surface = Capsule.TranslatePoint(_capsuleBounds.TopLeft, Root);
            Scene.ScrollToEnd();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var scrolled = await CaptureAsync("capsule-scrolled.png");
            bool translucent = mode is GlassLensMode.Shader or GlassLensMode.Fallback;
            if (Scene.VerticalOffset <= 0 || GlassColor.Color != original || (translucent && GlassDifference(first, scrolled, surface) < 15))
                throw new InvalidOperationException("Scrolling the page must visibly change the glass without changing the active cover.");
            if (Tracks.TranslatePoint(new Point(0, Tracks.ActualHeight), Root).Y > surface.Y)
                throw new InvalidOperationException("The last track must scroll clear of the capsule.");
            Scene.ScrollToHome();

            if (PlayButton.Template.FindName("Label", PlayButton) is not ContentPresenter label ||
                PlayButton.Template.FindName("Hover", PlayButton) is not Border hover || !ShellTransitions.GetPlaybackFeedback(label))
                throw new InvalidOperationException("The capsule playback button must use the player feedback template.");
            if (hover.Background is not SolidColorBrush plate || plate.Color.A != 0x16)
                throw new InvalidOperationException("The hover layer must retain the shared translucent brush.");
            ShellTransitions.SetHoverOpacity(hover, 1);
            ShellTransitions.SetPressPose(label, true);
            await Task.Delay(190);
            double pressedScale = ShellTransitions.GetReducedMotion(this) ? 1 : .9;
            if (hover.Opacity != 1 || label.RenderTransform is not ScaleTransform scale || Math.Abs(scale.ScaleX - pressedScale) > .001)
                throw new InvalidOperationException("The shared playback template must apply its hover and press states.");
            ShellTransitions.SetHoverOpacity(hover, 0);
            ShellTransitions.SetPressPose(label, false);
            await Task.Delay(40);
            double returningScale = scale.ScaleX;
            ShellTransitions.SetPressPose(label, true);
            if (Math.Abs(scale.ScaleX - returningScale) > .02)
                throw new InvalidOperationException("Pressing during release must continue from the current pose.");
            ShellTransitions.SetPressPose(label, false);
            PlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, PlayButton));
            await Task.Delay(300);
            if (!_ready || !_playing || _audio.Position <= TimeSpan.Zero || Math.Abs(scale.ScaleX - 1) > .001)
                throw new InvalidOperationException("The demo audio must advance and the button must return to its resting pose.");
            int layoutResizes = 0;
            bool seekFadedIn = false, seekFadedOut = false;
            void CountResize(object sender, SizeChangedEventArgs e) => layoutResizes++;
            void ObserveSeekFade(object? sender, EventArgs e)
            {
                if (!Seek.IsVisible || SeekClip.Rect.Width <= 0 || SeekSlot.Opacity is not (> .02 and < .98))
                    return;
                if (Expanded)
                    seekFadedIn = true;
                else
                    seekFadedOut = true;
            }
            Capsule.SizeChanged += CountResize;
            CompositionTarget.Rendering += ObserveSeekFade;
            toggle.Toggle();
            await Task.Delay(85);
            double widthBeforeReverse = _capsuleBounds.Width;
            double velocityBeforeReverse = _shape.Velocity;
            double opacityVelocityBeforeReverse = _details.Velocity;
            toggle.Toggle();
            if (!ShellTransitions.GetReducedMotion(this) && (widthBeforeReverse <= CompactWidth || widthBeforeReverse >= ExpandedWidth ||
                velocityBeforeReverse <= 0 || _capsuleBounds.Width != widthBeforeReverse || _shape.Velocity != velocityBeforeReverse ||
                _details.Velocity != opacityVelocityBeforeReverse))
                throw new InvalidOperationException("Reversing an active morph must retain its position and both spring velocities.");
            await Task.Delay(410);
            if (Expanded || Seek.IsVisible || PreviousButton.IsVisible || _capsuleBounds.Width != CompactWidth || _shapeRendering || ShapeArrow.Angle != 0)
                throw new InvalidOperationException("A reversed expansion must settle back to the compact bar.");
            toggle.Toggle();
            await Task.Delay(410);
            if (!Expanded || toggle.ToggleState != ToggleState.On || !Seek.IsVisible || !PreviousButton.IsVisible ||
                !Seek.IsTabStop || !PreviousButton.IsTabStop || Math.Abs(_capsuleBounds.Width - ExpandedWidth) > .7 || ShapeArrow.Angle != 180)
                throw new InvalidOperationException("The expanded bar must reveal the existing playback controls.");
            Capsule.SizeChanged -= CountResize;
            CompositionTarget.Rendering -= ObserveSeekFade;
            if (layoutResizes != 0)
                throw new InvalidOperationException("The glass outline must animate without resizing the layout.");
            if (!ShellTransitions.GetReducedMotion(this) && (!seekFadedIn || !seekFadedOut))
                throw new InvalidOperationException("The seek control must draw intermediate opacity frames on both transitions.");
            Seek.BorderCover.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            await Task.Delay(200);
            if (Math.Abs(Seek.BorderBackground.Opacity - .72) > .001 ||
                Math.Abs(Seek.GridBordersScaleTransform.ScaleY - (Seek.ReduceMotion ? 1 : 1.55)) > .001)
                throw new InvalidOperationException("Seek hover must reach the player opacity and scale.");
            Seek.BorderCover.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            Seek.BeginPreview(Seek.BorderCover.ActualWidth * .25);
            PressEscape();
            if (!Expanded || Seek.Dragging)
                throw new InvalidOperationException("Escape must end a seek drag before it closes the bar.");
            Seek.BeginPreview(Seek.BorderCover.ActualWidth * .25);
            Seek.CommitPreview();
            await Task.Delay(190);
            if (_audio.Position.TotalMilliseconds < 8900)
                throw new InvalidOperationException("The shared seek control must be wired to the demo audio.");
            TogglePlayback();
            SelectTrack(1);
            await Task.Delay(420);
            if (!_ready || GlassColor.Color == original || CapsuleTitle.Text != _tracks[1].Title || MiniCover.ImageSource != _tracks[1].Cover)
                throw new InvalidOperationException("Selecting a track must change both audio and the capsule tint.");
            await CaptureAsync("capsule-expanded.png");
            await VerifyToolsAsync();
            double wideWidth = Width;
            Width = MinWidth;
            Height = MinHeight;
            await Task.Delay(350);
            UpdateLayout();
            if (!Expanded || Seek.IsVisible || Seek.IsTabStop || SeekSlot.IsHitTestVisible || Math.Abs(_capsuleBounds.Height - CompactHeight) > .7)
                throw new InvalidOperationException("An expanded narrow capsule must keep one row and hide the seek control.");
            await CaptureAsync("capsule-narrow.png");
            if (SoundTool.IsVisible || !QueueTool.IsVisible || !LyricsTool.IsVisible)
                throw new InvalidOperationException("The narrow tool group must keep direct queue and lyric actions.");
            ClickTool(TrackStamp);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (!CompactSound.IsVisible)
                throw new InvalidOperationException("The narrow track menu must expose the sound controls.");
            ClickTool(CompactSound);
            if (!SoundOptions.IsVisible)
                throw new InvalidOperationException("The compact sound entry must open the same audio settings.");
            CloseToolFlyout();
            PressEscape();
            await Task.Delay(410);
            if (Expanded || Math.Abs(_capsuleBounds.Width - Math.Min(CompactWidth, Capsule.Width)) > .7 ||
                ShapeArrow.Angle != 0 || Seek.IsVisible || PreviousButton.IsVisible)
                throw new InvalidOperationException("The narrow capsule must collapse back to the compact layout.");
            bool reduced = ShellTransitions.GetReducedMotion(this);
            ApplyReducedMotion(true);
            toggle.Toggle();
            if (ShapeArrow.Angle != 180 || Seek.IsVisible || PreviousSlot.Opacity != 1 || _shapeRendering)
                throw new InvalidOperationException("Reduced motion must reveal the expanded controls immediately.");
            Width = wideWidth;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (!Expanded || !Seek.IsVisible || !Seek.IsTabStop || Seek.ActualWidth < MinimumSeekWidth - .7)
                throw new InvalidOperationException("Widening an expanded capsule must restore a full-size seek control.");
            toggle.Toggle();
            ApplyReducedMotion(reduced);

            GlassScene.SetReduceTransparency(Root, true);
            Root.InvalidateGlass();
            if (CapsuleGlass.Mode is not (GlassLensMode.Opaque or GlassLensMode.HighContrast) || ToolGlass.Mode != CapsuleGlass.Mode)
                throw new InvalidOperationException("Both glass surfaces must honor reduced transparency.");
            Root.ClearValue(GlassScene.ReduceTransparencyProperty);
            Root.InvalidateGlass();
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "glass-verification.txt"),
                [$"Material: {GlassScene.GetMaterial(CapsuleGlass)}", $"Live render modes: {CapsuleGlass.Mode}, {ToolGlass.Mode}",
                 $"Layout resizes during the morph: {layoutResizes}",
                 $"Motion: M3 Standard spatial and effects springs with {ShapeTimeScale:F1}× periods",
                 "Passed: glass geometry without relayout, pointer isolation, playback feedback, seek fades, reversal, responsive centered capsule, audio/queue/lyric actions, reduced motion and transparency",
                 "PNG capture: software fallback", GlassDiagnostics.Describe()]);
            Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "verification-failure.txt"), error.ToString());
            Application.Current.Shutdown(1);
        }
    }

    private static void ClickTool(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

    private async Task VerifyToolsAsync()
    {
        ClickTool(SoundTool);
        DemoVolume.Value = 32;
        if (!SoundOptions.IsVisible || _audio.Volume != .32 || SoundTool.IsChecked != true)
            throw new InvalidOperationException("The sound tool must control the active demo audio.");
        DemoVolume.Value = 0;
        ClickTool(QueueTool);
        QueueTracks.SelectedIndex = 2;
        ShuffleMode.IsChecked = true;
        ClickTool(ShuffleMode);
        ClickTool(RepeatModeButton);
        if (!QueueOptions.IsVisible || SoundOptions.IsVisible || !ReferenceEquals(QueueTracks.Items[0], _tracks[2]) ||
            Tracks.SelectedIndex != 2 || _repeatMode != DemoRepeat.Queue)
            throw new InvalidOperationException("The queue tool must select tracks and expose the active playback order.");
        await Task.Delay(220);
        await CaptureAsync("capsule-tools.png");
        ClickTool(LyricsTool);
        if (!LyricOptions.IsVisible || QueueOptions.IsVisible || DemoLyrics.Text != _tracks[2].Lyrics)
            throw new InvalidOperationException("The lyrics tool must show the active demo track.");
        ClickTool(TrackStamp);
        ClickTool(FavoriteToggle);
        if (!TrackOptions.IsVisible || !FavoriteMark.IsVisible || FavoriteToggle.IsChecked != true)
            throw new InvalidOperationException("The track menu must reflect its favorite state on the capsule.");
        PressEscape();
        await Task.Delay(180);
        if (ToolFlyout.IsVisible || TrackStamp.IsChecked == true || !Expanded)
            throw new InvalidOperationException("Escape must close the tool before collapsing the capsule.");
    }

    private void PressEscape() => RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, Key.Escape)
    {
        RoutedEvent = Keyboard.PreviewKeyDownEvent
    });

    private async Task<BitmapSource> CaptureAsync(string name)
    {
        var capsule = Capsule.TransformToVisual(Root).TransformBounds(_capsuleBounds);
        if (capsule.Bottom >= Root.ActualHeight || capsule.Left <= 0 || GlassScene.FindScene(CapsuleGlass) != Root)
            throw new InvalidOperationException("The capsule must fit inside its glass scene with space below.");
        var tools = ToolGroup.TransformToVisual(Root).TransformBounds(new Rect(ToolGroup.RenderSize));
        if (Math.Abs(capsule.Left + capsule.Right - Root.ActualWidth) > 1.5 || tools.Left - capsule.Right < ToolGap - 1.5 ||
            tools.Right > Root.ActualWidth || Math.Abs(capsule.Bottom - tools.Bottom) > 1 || GlassScene.FindScene(ToolGlass) != Root)
            throw new InvalidOperationException("The separate tool group must fit beside the centered capsule.");
        var blankGlass = Root.InputHitTest(new Point((capsule.Left + capsule.Right) / 2, capsule.Bottom - 3));
        var corner = Root.InputHitTest(new Point(capsule.Left + 1, capsule.Top + 1));
        if (blankGlass != Capsule || (corner is DependencyObject target && (target == Capsule || Capsule.IsAncestorOf(target))))
            throw new InvalidOperationException("Blank glass must intercept input within the rounded capsule outline.");
        var transport = Transport.TransformToVisual(CapsuleContent).TransformBounds(new Rect(Transport.RenderSize));
        var options = ShapeButton.TransformToVisual(CapsuleContent).TransformBounds(new Rect(ShapeButton.RenderSize));
        var stamp = TrackStamp.TransformToVisual(CapsuleContent).TransformBounds(new Rect(TrackStamp.RenderSize));
        var progress = Seek.IsVisible ? Seek.TransformToVisual(CapsuleContent).TransformBounds(new Rect(Seek.RenderSize)) : Rect.Empty;
        var contentBounds = Capsule.TransformToVisual(CapsuleContent).TransformBounds(_capsuleBounds);
        contentBounds.Inflate(.7, .7);
        if (!contentBounds.Contains(stamp) || !contentBounds.Contains(options) || !contentBounds.Contains(transport) ||
            transport.IntersectsWith(options) || stamp.IntersectsWith(transport) ||
            (Seek.IsVisible && (progress.IntersectsWith(stamp) || progress.IntersectsWith(transport) || Seek.ActualWidth < 319)))
            throw new InvalidOperationException("Playback and shape controls must fit without overlap.");
        var image = new RenderTargetBitmap((int)Root.ActualWidth, (int)Root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        // WPF bitmap capture cannot draw the upstream pixel shader 3.0 path.
        GlassScene.SetRenderMode(CapsuleGlass, GlassRenderMode.Fallback);
        GlassScene.SetRenderMode(ToolGlass, GlassRenderMode.Fallback);
        try
        {
            Root.InvalidateGlass();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (CapsuleGlass.Mode == GlassLensMode.Shader || ToolGlass.Mode == GlassLensMode.Shader)
                throw new InvalidOperationException("Bitmap capture must use the rendered software material.");
            image.Render(Root);
        }
        finally
        {
            CapsuleGlass.ClearValue(GlassScene.RenderModeProperty);
            ToolGlass.ClearValue(GlassScene.RenderModeProperty);
            Root.InvalidateGlass();
        }
        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, name), Artwork.Encode(image));
        return image;
    }

    private static int GlassDifference(BitmapSource first, BitmapSource second, Point surface)
    {
        var sample = new Int32Rect((int)surface.X + 64, (int)surface.Y + 7, 1, 1);
        byte[] before = new byte[4], after = new byte[4];
        first.CopyPixels(sample, before, 4, 0);
        second.CopyPixels(sample, after, 4, 0);
        return Math.Abs(before[0] - after[0]) + Math.Abs(before[1] - after[1]) + Math.Abs(before[2] - after[2]);
    }
}
