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
    private const double CompactWidth = 384, ExpandedWidth = 664, CompactHeight = 54;
    private sealed record DemoTrack(string Title, string Artist, string Duration, BitmapSource Cover, Uri Audio, double DurationMs);

    private readonly MediaPlayer _audio = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DemoTrack[] _tracks;
    private bool _playing, _ready;
    private bool Expanded => ShapeButton.IsChecked == true;
    private bool? _displayedPlaying;
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    // SpringOptions takes the period and bounce corresponding to the M3 stiffness and damping ratio.
    private static readonly SpringOptions SpatialSpring = new(Math.Tau / Math.Sqrt(700), 1 - .9);
    private static readonly SpringOptions EffectsSpring = new(Math.Tau / Math.Sqrt(3800), 0);
    private readonly Spring _shape = new(0, SpatialSpring), _details = new(0, EffectsSpring);
    // shortcut: The pinned glass renderer exposes its outline hook internally, replace this when it becomes public.
    private static readonly Action<GlassLens, Rect?> SetGlassShape = typeof(GlassLens)
        .GetMethod("SetShape", BindingFlags.Instance | BindingFlags.NonPublic)!
        .CreateDelegate<Action<GlassLens, Rect?>>();
    private readonly TranslateTransform _trackShift = new(), _transportShift = new();
    private Rect _capsuleBounds = Rect.Empty;
    private bool _stacked;
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
            ("沿光而行", "Lyric Lab", 36000d),
            ("回声之间", "Voice A / Voice B", 32000d),
            ("Slow Motion", "Lyric Lab", 44000d)
        };
        _tracks = info.Select((item, index) => new DemoTrack(item.Item1, item.Item2, Timeline.Format(item.Item3),
            Artwork.Create(index), new Uri(Path.Combine(assetDirectory, $"track-{index + 1}.wav")), item.Item3)).ToArray();
        _audio.Volume = .4;
        _audio.MediaOpened += (_, _) =>
        {
            _ready = true;
            PlayButton.IsEnabled = Seek.IsEnabled = true;
            if (_playing)
                _audio.Play();
        };
        _audio.MediaEnded += (_, _) => SelectTrack(1);
        _audio.MediaFailed += (_, _) =>
        {
            _ready = false;
            PlayButton.IsEnabled = Seek.IsEnabled = false;
            _playing = false;
            UpdatePlayIcon();
        };
        _clock.Tick += (_, _) => RefreshClock();
        Tracks.ItemsSource = _tracks;
        Tracks.SelectedIndex = 0;
        UpdatePlayIcon();
        Root.SizeChanged += (_, _) => ArrangeCapsule();
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
    private void SelectTrack(int offset) => Tracks.SelectedIndex = (Tracks.SelectedIndex + offset + _tracks.Length) % _tracks.Length;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Seek.Dragging)
            Seek.CancelPreview();
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

        SeekSlot.IsHitTestVisible = PreviousSlot.IsHitTestVisible = expanded;
        Seek.IsTabStop = PreviousButton.IsTabStop = expanded;
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

        double expandedWidth = Math.Min(available, ExpandedWidth);
        _stacked = expandedWidth < 656;
        Capsule.Width = expandedWidth;
        Capsule.Height = CompactHeight + (_stacked ? Seek.Height : 0);
        Grid.SetRow(SeekSlot, _stacked ? 1 : 0);
        Grid.SetColumn(SeekSlot, _stacked ? 0 : 1);
        Grid.SetColumnSpan(SeekSlot, _stacked ? 4 : 1);
        Seek.Width = _stacked ? expandedWidth - 60 : 320;
        SeekSlot.Width = Seek.Width + Seek.Margin.Left + Seek.Margin.Right;
        SeekSlot.Height = Seek.Height;
        PreviousSlot.Width = PreviousButton.Width;
        double buttons = PreviousButton.Width + PlayButton.Width + PlayButton.Margin.Left + PlayButton.Margin.Right +
            NextButton.Width + Transport.Margin.Left + Transport.Margin.Right + ShapeButton.Width;
        if (double.IsFinite(buttons))
            TrackStamp.MaxWidth = _stacked ? Math.Max(34, Math.Min(available, CompactWidth) - buttons -
                CapsuleContent.Margin.Left - CapsuleContent.Margin.Right - TrackStamp.Margin.Right) : double.PositiveInfinity;
        PresentCapsule();
    }

    private void PresentCapsule()
    {
        double progress = Math.Clamp(_shape.Value, 0, 1);
        double inset = Math.Max(0, Capsule.Width - CompactWidth) * (1 - progress) / 2;
        double lower = Math.Max(0, Capsule.Height - CompactHeight) * (1 - progress);
        var bounds = new Rect(inset, lower, Capsule.Width - 2 * inset, Capsule.Height - lower);
        if (_capsuleBounds != bounds)
        {
            _capsuleBounds = bounds;
            SetGlassShape(CapsuleGlass, bounds);
            ShadeBounds.Rect = new Rect(bounds.Left + 1, bounds.Top + 1, Math.Max(0, bounds.Width - 2), bounds.Height - 2);
            ShadeBounds.RadiusX = ShadeBounds.RadiusY = bounds.Height / 2 - 1;
            _trackShift.X = inset;
            _transportShift.X = -inset;
            _trackShift.Y = _transportShift.Y = lower;
            double trim = Math.Min(inset, SeekSlot.Width / 2);
            SeekClip.Rect = _stacked
                ? new Rect(0, lower, SeekSlot.Width, Math.Max(0, Seek.Height - lower))
                : new Rect(trim, 0, Math.Max(0, SeekSlot.Width - 2 * trim), Seek.Height);
            ShapeArrow.Angle = 180 * progress;
        }
        SeekSlot.Opacity = PreviousSlot.Opacity = Math.Clamp(_details.Value, 0, 1);
        SeekSlot.Visibility = PreviousSlot.Visibility = Expanded || _shapeRendering ? Visibility.Visible : Visibility.Hidden;
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
        GlassShade.Visibility = contrast ? Visibility.Collapsed : Visibility.Visible;
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
            if (new ToggleButtonAutomationPeer(ShapeButton).GetPattern(PatternInterface.Toggle) is not IToggleProvider toggle ||
                toggle.ToggleState != ToggleState.Off)
                throw new InvalidOperationException("The playback disclosure must expose its collapsed state to UI Automation.");
            var first = await CaptureAsync("capsule-preview.png");
            if (Math.Abs(_capsuleBounds.Width - CompactWidth) > .7 || Math.Abs(_capsuleBounds.Height - CompactHeight) > .7 ||
                Seek.IsVisible || Seek.IsTabStop || PreviousButton.IsTabStop || PlayButton.Content is not Image { Source: not null } ||
                !ReferenceEquals(PlayButton.Style, FindResource("Shell.PlaybackButton")))
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
            if (!SystemParameters.HighContrast && (hover.Background is not SolidColorBrush plate || plate.Color != Color.FromRgb(38, 38, 40)))
                throw new InvalidOperationException($"The hover plate must match the player's composited color, style brush {ShellAssist.GetHoverBrush(PlayButton)}, drawn brush {hover.Background}.");
            ShellTransitions.SetHoverOpacity(hover, 1);
            ShellTransitions.SetPressPose(label, true);
            await Task.Delay(190);
            double pressedScale = ShellTransitions.GetReducedMotion(this) ? 1 : .9;
            if (hover.Opacity != 1 || label.RenderTransform is not ScaleTransform scale || Math.Abs(scale.ScaleX - pressedScale) > .001)
                throw new InvalidOperationException("The shared playback template must apply its hover and press states.");
            ShellTransitions.SetHoverOpacity(hover, 0);
            ShellTransitions.SetPressPose(label, false);
            PlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, PlayButton));
            await Task.Delay(300);
            if (!_ready || !_playing || _audio.Position <= TimeSpan.Zero)
                throw new InvalidOperationException("The demo audio must play and advance.");
            int layoutResizes = 0;
            void CountResize(object sender, SizeChangedEventArgs e) => layoutResizes++;
            Capsule.SizeChanged += CountResize;
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
            await Task.Delay(300);
            if (Expanded || Seek.IsVisible || PreviousButton.IsVisible || _capsuleBounds.Width != CompactWidth || _shapeRendering || ShapeArrow.Angle != 0)
                throw new InvalidOperationException("A reversed expansion must settle back to the compact bar.");
            toggle.Toggle();
            await Task.Delay(320);
            if (!Expanded || toggle.ToggleState != ToggleState.On || !Seek.IsVisible || !PreviousButton.IsVisible ||
                !Seek.IsTabStop || !PreviousButton.IsTabStop || Math.Abs(_capsuleBounds.Width - ExpandedWidth) > .7 || ShapeArrow.Angle != 180)
                throw new InvalidOperationException("The expanded bar must reveal the existing playback controls.");
            Capsule.SizeChanged -= CountResize;
            if (layoutResizes != 0)
                throw new InvalidOperationException("The glass outline must animate without resizing the layout.");
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
            Width = MinWidth;
            Height = MinHeight;
            await Task.Delay(350);
            UpdateLayout();
            await CaptureAsync("capsule-narrow.png");
            PressEscape();
            await Task.Delay(300);
            if (Expanded || Math.Abs(_capsuleBounds.Width - CompactWidth) > .7 || Seek.IsVisible || PreviousButton.IsVisible)
                throw new InvalidOperationException("The narrow capsule must collapse back to the compact layout.");
            bool reduced = ShellTransitions.GetReducedMotion(this);
            ApplyReducedMotion(true);
            toggle.Toggle();
            if (_capsuleBounds.Width <= CompactWidth || SeekSlot.Opacity != 1 || PreviousSlot.Opacity != 1 || _shapeRendering)
                throw new InvalidOperationException("Reduced motion must reveal the expanded controls immediately.");
            toggle.Toggle();
            ApplyReducedMotion(reduced);

            GlassScene.SetReduceTransparency(Root, true);
            Root.InvalidateGlass();
            if (CapsuleGlass.Mode is not (GlassLensMode.Opaque or GlassLensMode.HighContrast))
                throw new InvalidOperationException("The capsule must honor the upstream reduced-transparency material.");
            Root.ClearValue(GlassScene.ReduceTransparencyProperty);
            Root.InvalidateGlass();
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "glass-verification.txt"),
                [$"Material: {GlassScene.GetMaterial(CapsuleGlass)}", $"Live render mode: {CapsuleGlass.Mode}",
                 $"Layout resizes during the morph: {layoutResizes}",
                 "Motion: M3 Standard default spatial (700, 0.9) and fast effects (3800, 1.0)",
                 "Passed: shared button hover and press, playback, shared seek hover, scrolling, reversal, UI Automation, Escape, narrow layout, reduced motion and transparency",
                 "PNG capture: software fallback", GlassDiagnostics.Describe()]);
            Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "verification-failure.txt"), error.ToString());
            Application.Current.Shutdown(1);
        }
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
        var transport = Transport.TransformToVisual(CapsuleContent).TransformBounds(new Rect(Transport.RenderSize));
        var options = ShapeButton.TransformToVisual(CapsuleContent).TransformBounds(new Rect(ShapeButton.RenderSize));
        var stamp = TrackStamp.TransformToVisual(CapsuleContent).TransformBounds(new Rect(TrackStamp.RenderSize));
        var progress = Seek.IsVisible ? Seek.TransformToVisual(CapsuleContent).TransformBounds(new Rect(Seek.RenderSize)) : Rect.Empty;
        if (transport.IntersectsWith(options) || stamp.IntersectsWith(transport) ||
            (Seek.IsVisible && (progress.IntersectsWith(stamp) || progress.IntersectsWith(transport) || Seek.ActualWidth < 319)))
            throw new InvalidOperationException("Playback and shape controls must fit without overlap.");
        var image = new RenderTargetBitmap((int)Root.ActualWidth, (int)Root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        // WPF bitmap capture cannot draw the upstream pixel shader 3.0 path.
        GlassScene.SetRenderMode(CapsuleGlass, GlassRenderMode.Fallback);
        try
        {
            Root.InvalidateGlass();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (CapsuleGlass.Mode == GlassLensMode.Shader)
                throw new InvalidOperationException("Bitmap capture must use the rendered software material.");
            image.Render(Root);
        }
        finally
        {
            CapsuleGlass.ClearValue(GlassScene.RenderModeProperty);
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
