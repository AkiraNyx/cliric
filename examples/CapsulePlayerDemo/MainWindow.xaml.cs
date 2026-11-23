using System.ComponentModel;
using System.IO;
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
using Cliric.App;
using Timeline = Cliric.Core.Timeline;
using static Cliric.App.PlaybackCapsule;

namespace CapsulePlayerDemo;

public partial class MainWindow : Window
{
    private sealed record DemoTrack(string Title, string Artist, string Duration, BitmapSource Cover, Uri Audio, double DurationMs, string Lyrics);

    private readonly MediaPlayer _audio = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DemoTrack[] _tracks;
    private bool _playing, _ready;
    private bool Expanded => PlaybackBar.Expanded;
    private bool? _displayedPlaying;
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    public MainWindow()
    {
        InitializeComponent();
        FontFamily = new FontFamily(new Uri("pack://application:,,,/Cliric;component/"), "./Assets/Fonts/#MiSans VF, Microsoft YaHei UI");
        PlaybackBar.InitializeControls();
        PlaybackBar.PreviousButton.Click += Previous;
        PlaybackBar.PlayButton.Click += TogglePlay;
        PlaybackBar.NextButton.Click += Next;
        PlaybackBar.TrackStamp.Tag = TrackOptions;
        PlaybackBar.SoundTool.Tag = SoundOptions;
        PlaybackBar.QueueTool.Tag = QueueOptions;
        PlaybackBar.LyricsTool.Tag = LyricOptions;
        foreach (var tool in new[] { PlaybackBar.TrackStamp, PlaybackBar.SoundTool, PlaybackBar.QueueTool, PlaybackBar.LyricsTool })
            tool.Click += ToggleTool;
        PlaybackBar.LayoutChanged += UpdateToolLayout;
        PlaybackBar.Seek.IsRealTimeUpdate = true;
        PlaybackBar.Seek.SeekRequested += OnSeek;
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
            PlaybackBar.PlayButton.IsEnabled = PlaybackBar.Seek.IsEnabled = true;
            if (_playing)
                _audio.Play();
        };
        _audio.MediaEnded += (_, _) => SelectTrack(1, atEnd: true);
        _audio.MediaFailed += (_, _) =>
        {
            _ready = false;
            PlaybackBar.PlayButton.IsEnabled = PlaybackBar.Seek.IsEnabled = false;
            _playing = false;
            UpdatePlayIcon();
        };
        _clock.Tick += (_, _) => RefreshClock();
        QueueTracks.ItemsSource = Tracks.ItemsSource = _tracks;
        Tracks.SelectedIndex = 0;
        UpdatePlayIcon();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                PlaybackBar.AnimateShape(immediate: true);
        };
        Loaded += (_, _) =>
        {
            PlaybackBar.AnimateShape(immediate: true);
            if (Environment.GetCommandLineArgs().Contains("--verify"))
                Dispatcher.InvokeAsync(VerifyPreview, DispatcherPriority.ApplicationIdle);
            else
                _clock.Start();
        };
        Closed += (_, _) =>
        {
            _clock.Stop();
            _audio.Close();
            SystemParameters.StaticPropertyChanged -= OnSystemPreferencesChanged;
        };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += (_, _) =>
        {
            if (_toolAnchor is not null && !ToolFlyout.IsMouseOver && !PlaybackBar.ToolGroup.IsMouseOver && !PlaybackBar.TrackStamp.IsMouseOver)
                CloseToolFlyout();
        };
    }

    private void OnTrackSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Tracks.SelectedItem is not DemoTrack track)
            return;
        _ready = false;
        PlaybackBar.PlayButton.IsEnabled = PlaybackBar.Seek.IsEnabled = false;
        _audio.Open(track.Audio);
        HeroTitle.Text = PlaybackBar.CapsuleTitle.Text = track.Title;
        HeroArtist.Text = PlaybackBar.CapsuleArtist.Text = track.Artist;
        PlaybackBar.TrackStamp.ToolTip = $"{track.Title}\n{track.Artist}";
        QueueTracks.SelectedItem = track;
        TrackOptionTitle.Text = LyricTitle.Text = track.Title;
        DemoLyrics.Text = track.Lyrics;
        UpdateFavorite();
        CurrentCover.ImageSource = track.Cover;
        Color tint = PlaybackBar.SetArtwork(track.Cover);
        AnimateColor(SceneColor, GradientStop.ColorProperty, Color.FromRgb((byte)(tint.R * .43), (byte)(tint.G * .43), (byte)(tint.B * .43)));
        PlaybackBar.Seek.CancelPreview();
        PlaybackBar.Seek.SetPosition(0, track.DurationMs);
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
            PlaybackBar.Seek.SetPosition(_audio.Position.TotalMilliseconds, _tracks[Tracks.SelectedIndex].DurationMs);
    }

    private void OnSeek(double position)
    {
        if (_ready && Cliric.App.MainWindow.PlayerSeekTarget(position, PlaybackBar.Seek.TotalTime, PlaybackBar.Seek.Dragging) is { } target)
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
        ShellPlaybackControls.SetPlaying(PlaybackBar.PlayButton, _playing, _displayedPlaying is { } previous && previous != _playing,
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
        if (e.Key == Key.Escape && PlaybackBar.Seek.Dragging)
            PlaybackBar.Seek.CancelPreview();
        else if (e.Key == Key.Escape && _toolAnchor is { } anchor)
        {
            CloseToolFlyout();
            anchor.Focus();
        }
        else if (e.Key == Key.Escape && Expanded)
            PlaybackBar.ShapeButton.IsChecked = false;
        else if (e.Key == Key.Space && !e.IsRepeat && e.KeyboardDevice.Modifiers == ModifierKeys.None &&
                 e.OriginalSource is not ButtonBase and not TextBoxBase)
            TogglePlayback();
        else
            return;
        e.Handled = true;
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
        PlaybackBar.ApplyPalette(_playing);
    }

    private void ApplyReducedMotion(bool reduced)
    {
        ShellTransitions.SetReducedMotion(this, reduced);
        PlaybackBar.ApplyMotionPreference(reduced);
        if (reduced)
        {
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
            var mode = PlaybackBar.CapsuleGlass.Mode;
            if (PlaybackBar.ToolGlass.Mode != mode || PlaybackBar.ToolGlass.Refraction != PlaybackBar.CapsuleGlass.Refraction ||
                GlassScene.GetMaterial(PlaybackBar.ToolGlass) != GlassScene.GetMaterial(PlaybackBar.CapsuleGlass))
                throw new InvalidOperationException("Both capsules must use the same glass material and rendering path.");
            if (new ToggleButtonAutomationPeer(PlaybackBar.ShapeButton).GetPattern(PatternInterface.Toggle) is not IToggleProvider toggle ||
                toggle.ToggleState != ToggleState.Off)
                throw new InvalidOperationException("The playback disclosure must expose its collapsed state to UI Automation.");
            var first = await CaptureAsync("capsule-preview.png");
            if (Math.Abs(PlaybackBar.CapsuleBounds.Width - CompactWidth) > .7 || Math.Abs(PlaybackBar.CapsuleBounds.Height - CompactHeight) > .7 ||
                PlaybackBar.Seek.IsVisible || PlaybackBar.Seek.IsTabStop || PlaybackBar.PreviousButton.IsTabStop || PlaybackBar.PlayButton.Content is not Image { Source: not null } ||
                !ReferenceEquals(PlaybackBar.PlayButton.Style, FindResource("Shell.IconButton")))
                throw new InvalidOperationException("The compact bar must use the regular playback controls at 384 × 54 DIP.");
            var original = PlaybackBar.GlassColor.Color;
            var surface = PlaybackBar.Capsule.TranslatePoint(PlaybackBar.CapsuleBounds.TopLeft, Root);
            Scene.ScrollToEnd();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var scrolled = await CaptureAsync("capsule-scrolled.png");
            bool translucent = mode is GlassLensMode.Shader or GlassLensMode.Fallback;
            if (Scene.VerticalOffset <= 0 || PlaybackBar.GlassColor.Color != original || (translucent && GlassDifference(first, scrolled, surface) < 15))
                throw new InvalidOperationException("Scrolling the page must visibly change the glass without changing the active cover.");
            if (Tracks.TranslatePoint(new Point(0, Tracks.ActualHeight), Root).Y > surface.Y)
                throw new InvalidOperationException("The last track must scroll clear of the capsule.");
            Scene.ScrollToHome();

            if (PlaybackBar.PlayButton.Template.FindName("Label", PlaybackBar.PlayButton) is not ContentPresenter label ||
                PlaybackBar.PlayButton.Template.FindName("Hover", PlaybackBar.PlayButton) is not Border hover || !ShellTransitions.GetPlaybackFeedback(label))
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
            PlaybackBar.PlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, PlaybackBar.PlayButton));
            await Task.Delay(300);
            if (!_ready || !_playing || _audio.Position <= TimeSpan.Zero || Math.Abs(scale.ScaleX - 1) > .001)
                throw new InvalidOperationException("The demo audio must advance and the button must return to its resting pose.");
            int layoutResizes = 0;
            bool seekFadedIn = false, seekFadedOut = false;
            void CountResize(object sender, SizeChangedEventArgs e) => layoutResizes++;
            void ObserveSeekFade(object? sender, EventArgs e)
            {
                if (!PlaybackBar.Seek.IsVisible || PlaybackBar.SeekClip.Rect.Width <= 0 || PlaybackBar.SeekSlot.Opacity is not (> .02 and < .98))
                    return;
                if (Expanded)
                    seekFadedIn = true;
                else
                    seekFadedOut = true;
            }
            PlaybackBar.Capsule.SizeChanged += CountResize;
            CompositionTarget.Rendering += ObserveSeekFade;
            toggle.Toggle();
            await Task.Delay(85);
            double widthBeforeReverse = PlaybackBar.CapsuleBounds.Width;
            double velocityBeforeReverse = PlaybackBar.ExpansionVelocity;
            double opacityVelocityBeforeReverse = PlaybackBar.DetailVelocity;
            toggle.Toggle();
            if (!ShellTransitions.GetReducedMotion(this) && (widthBeforeReverse <= CompactWidth || widthBeforeReverse >= ExpandedWidth ||
                velocityBeforeReverse <= 0 || PlaybackBar.CapsuleBounds.Width != widthBeforeReverse || PlaybackBar.ExpansionVelocity != velocityBeforeReverse ||
                PlaybackBar.DetailVelocity != opacityVelocityBeforeReverse))
                throw new InvalidOperationException("Reversing an active morph must retain its position and both spring velocities.");
            await Task.Delay(410);
            if (Expanded || PlaybackBar.Seek.IsVisible || PlaybackBar.PreviousButton.IsVisible || PlaybackBar.CapsuleBounds.Width != CompactWidth || PlaybackBar.ShapeRendering || PlaybackBar.ShapeArrow.Angle != 0)
                throw new InvalidOperationException("A reversed expansion must settle back to the compact bar.");
            toggle.Toggle();
            await Task.Delay(410);
            if (!Expanded || toggle.ToggleState != ToggleState.On || !PlaybackBar.Seek.IsVisible || !PlaybackBar.PreviousButton.IsVisible ||
                !PlaybackBar.Seek.IsTabStop || !PlaybackBar.PreviousButton.IsTabStop || Math.Abs(PlaybackBar.CapsuleBounds.Width - PlaybackBar.Capsule.Width) > .7 || PlaybackBar.ShapeArrow.Angle != 180)
                throw new InvalidOperationException("The expanded bar must reveal the existing playback controls.");
            PlaybackBar.Capsule.SizeChanged -= CountResize;
            CompositionTarget.Rendering -= ObserveSeekFade;
            if (layoutResizes != 0)
                throw new InvalidOperationException("The glass outline must animate without resizing the layout.");
            if (!ShellTransitions.GetReducedMotion(this) && (!seekFadedIn || !seekFadedOut))
                throw new InvalidOperationException("The seek control must draw intermediate opacity frames on both transitions.");
            PlaybackBar.Seek.BorderCover.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            await Task.Delay(200);
            if (Math.Abs(PlaybackBar.Seek.BorderBackground.Opacity - .72) > .001 ||
                Math.Abs(PlaybackBar.Seek.GridBordersScaleTransform.ScaleY - (PlaybackBar.Seek.ReduceMotion ? 1 : 1.55)) > .001)
                throw new InvalidOperationException("Seek hover must reach the player opacity and scale.");
            PlaybackBar.Seek.BorderCover.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            PlaybackBar.Seek.BeginPreview(PlaybackBar.Seek.BorderCover.ActualWidth * .25);
            PressEscape();
            if (!Expanded || PlaybackBar.Seek.Dragging)
                throw new InvalidOperationException("Escape must end a seek drag before it closes the bar.");
            PlaybackBar.Seek.BeginPreview(PlaybackBar.Seek.BorderCover.ActualWidth * .25);
            PlaybackBar.Seek.CommitPreview();
            await Task.Delay(190);
            if (_audio.Position.TotalMilliseconds < 8900)
                throw new InvalidOperationException("The shared seek control must be wired to the demo audio.");
            TogglePlayback();
            SelectTrack(1);
            await Task.Delay(420);
            if (!_ready || PlaybackBar.GlassColor.Color == original || PlaybackBar.CapsuleTitle.Text != _tracks[1].Title || PlaybackBar.MiniCover.ImageSource != _tracks[1].Cover)
                throw new InvalidOperationException("Selecting a track must change both audio and the capsule tint.");
            await CaptureAsync("capsule-expanded.png");
            await VerifyToolsAsync();
            double wideWidth = Width;
            Width = MinWidth;
            Height = MinHeight;
            await Task.Delay(350);
            UpdateLayout();
            if (!Expanded || PlaybackBar.Seek.IsVisible || PlaybackBar.Seek.IsTabStop || PlaybackBar.SeekSlot.IsHitTestVisible || Math.Abs(PlaybackBar.CapsuleBounds.Height - CompactHeight) > .7)
                throw new InvalidOperationException("An expanded narrow capsule must keep one row and hide the seek control.");
            await CaptureAsync("capsule-narrow.png");
            if (PlaybackBar.SoundTool.IsVisible || !PlaybackBar.QueueTool.IsVisible || !PlaybackBar.LyricsTool.IsVisible)
                throw new InvalidOperationException("The narrow tool group must keep direct queue and lyric actions.");
            ClickTool(PlaybackBar.TrackStamp);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (!CompactSound.IsVisible)
                throw new InvalidOperationException("The narrow track menu must expose the sound controls.");
            ClickTool(CompactSound);
            if (!SoundOptions.IsVisible)
                throw new InvalidOperationException("The compact sound entry must open the same audio settings.");
            CloseToolFlyout();
            PressEscape();
            await Task.Delay(410);
            if (Expanded || Math.Abs(PlaybackBar.CapsuleBounds.Width - Math.Min(CompactWidth, PlaybackBar.Capsule.Width)) > .7 ||
                PlaybackBar.ShapeArrow.Angle != 0 || PlaybackBar.Seek.IsVisible || PlaybackBar.PreviousButton.IsVisible)
                throw new InvalidOperationException("The narrow capsule must collapse back to the compact layout.");
            bool reduced = ShellTransitions.GetReducedMotion(this);
            ApplyReducedMotion(true);
            toggle.Toggle();
            if (PlaybackBar.ShapeArrow.Angle != 180 || PlaybackBar.Seek.IsVisible || PlaybackBar.PreviousSlot.Opacity != 1 || PlaybackBar.ShapeRendering)
                throw new InvalidOperationException("Reduced motion must reveal the expanded controls immediately.");
            Width = wideWidth;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (!Expanded || !PlaybackBar.Seek.IsVisible || !PlaybackBar.Seek.IsTabStop || PlaybackBar.Seek.ActualWidth < MinimumSeekWidth - .7)
                throw new InvalidOperationException("Widening an expanded capsule must restore a full-size seek control.");
            toggle.Toggle();
            ApplyReducedMotion(reduced);

            GlassScene.SetReduceTransparency(Root, true);
            Root.InvalidateGlass();
            if (PlaybackBar.CapsuleGlass.Mode is not (GlassLensMode.Opaque or GlassLensMode.HighContrast) || PlaybackBar.ToolGlass.Mode != PlaybackBar.CapsuleGlass.Mode)
                throw new InvalidOperationException("Both glass surfaces must honor reduced transparency.");
            Root.ClearValue(GlassScene.ReduceTransparencyProperty);
            Root.InvalidateGlass();
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "glass-verification.txt"),
                [$"Material: {GlassScene.GetMaterial(PlaybackBar.CapsuleGlass)}", $"Live render modes: {PlaybackBar.CapsuleGlass.Mode}, {PlaybackBar.ToolGlass.Mode}",
                 $"Layout resizes during the morph: {layoutResizes}",
                 $"Motion: M3 Standard spatial and effects springs with {ShapeTimeScale:F1}× periods",
                 "Passed: glass morph without relayout, pointer isolation, playback feedback, seek fades, reversal, responsive centered capsule, audio/queue/lyric actions, reduced motion and transparency",
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
        ClickTool(PlaybackBar.SoundTool);
        DemoVolume.Value = 32;
        if (!SoundOptions.IsVisible || _audio.Volume != .32 || PlaybackBar.SoundTool.IsChecked != true)
            throw new InvalidOperationException("The sound tool must control the active demo audio.");
        DemoVolume.Value = 0;
        ClickTool(PlaybackBar.QueueTool);
        QueueTracks.SelectedIndex = 2;
        ShuffleMode.IsChecked = true;
        ClickTool(ShuffleMode);
        ClickTool(RepeatModeButton);
        if (!QueueOptions.IsVisible || SoundOptions.IsVisible || !ReferenceEquals(QueueTracks.Items[0], _tracks[2]) ||
            Tracks.SelectedIndex != 2 || _repeatMode != DemoRepeat.Queue)
            throw new InvalidOperationException("The queue tool must select tracks and expose the active playback order.");
        await Task.Delay(220);
        await CaptureAsync("capsule-tools.png");
        ClickTool(PlaybackBar.LyricsTool);
        if (!LyricOptions.IsVisible || QueueOptions.IsVisible || DemoLyrics.Text != _tracks[2].Lyrics)
            throw new InvalidOperationException("The lyrics tool must show the active demo track.");
        ClickTool(PlaybackBar.TrackStamp);
        ClickTool(FavoriteToggle);
        if (!TrackOptions.IsVisible || !PlaybackBar.FavoriteMark.IsVisible || FavoriteToggle.IsChecked != true)
            throw new InvalidOperationException("The track menu must reflect its favorite state on the capsule.");
        PressEscape();
        await Task.Delay(180);
        if (ToolFlyout.IsVisible || PlaybackBar.TrackStamp.IsChecked == true || !Expanded)
            throw new InvalidOperationException("Escape must close the tool before collapsing the capsule.");
    }

    private void PressEscape() => RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, Key.Escape)
    {
        RoutedEvent = Keyboard.PreviewKeyDownEvent
    });

    private async Task<BitmapSource> CaptureAsync(string name)
    {
        var capsule = PlaybackBar.Capsule.TransformToVisual(Root).TransformBounds(PlaybackBar.CapsuleBounds);
        if (capsule.Bottom >= Root.ActualHeight || capsule.Left <= 0 || GlassScene.FindScene(PlaybackBar.CapsuleGlass) != Root)
            throw new InvalidOperationException("The capsule must fit inside its glass scene with space below.");
        var tools = PlaybackBar.ToolGroup.TransformToVisual(Root).TransformBounds(new Rect(PlaybackBar.ToolGroup.RenderSize));
        if (Math.Abs(capsule.Left + capsule.Right - Root.ActualWidth) > 1.5 || tools.Left - capsule.Right < ToolGap - 1.5 ||
            tools.Right > Root.ActualWidth || Math.Abs(capsule.Bottom - tools.Bottom) > 1 || GlassScene.FindScene(PlaybackBar.ToolGlass) != Root)
            throw new InvalidOperationException("The separate tool group must fit beside the centered capsule.");
        var blankGlass = Root.InputHitTest(new Point((capsule.Left + capsule.Right) / 2, capsule.Bottom - 3));
        var corner = Root.InputHitTest(new Point(capsule.Left + 1, capsule.Top + 1));
        var blankToolGlass = Root.InputHitTest(new Point((tools.Left + tools.Right) / 2, tools.Bottom - 3));
        var toolCorner = Root.InputHitTest(new Point(tools.Left + 1, tools.Top + 1));
        if (blankGlass != PlaybackBar.CapsuleHitArea || (corner is DependencyObject target && PlaybackBar.Capsule.IsAncestorOf(target)) ||
            blankToolGlass != PlaybackBar.ToolHitArea || (toolCorner is DependencyObject toolTarget && PlaybackBar.ToolGroup.IsAncestorOf(toolTarget)))
            throw new InvalidOperationException("Blank glass must intercept input within the rounded capsule outline.");
        var transport = PlaybackBar.Transport.TransformToVisual(PlaybackBar.CapsuleContent).TransformBounds(new Rect(PlaybackBar.Transport.RenderSize));
        if (!PlaybackBar.PreviousButton.IsVisible)
        {
            transport.X += PlaybackBar.PreviousSlot.Width;
            transport.Width -= PlaybackBar.PreviousSlot.Width;
        }
        var options = PlaybackBar.ShapeButton.TransformToVisual(PlaybackBar.CapsuleContent).TransformBounds(new Rect(PlaybackBar.ShapeButton.RenderSize));
        var stamp = PlaybackBar.TrackStamp.TransformToVisual(PlaybackBar.CapsuleContent).TransformBounds(new Rect(PlaybackBar.TrackStamp.RenderSize));
        var progress = PlaybackBar.Seek.IsVisible ? PlaybackBar.Seek.TransformToVisual(PlaybackBar.CapsuleContent).TransformBounds(new Rect(PlaybackBar.Seek.RenderSize)) : Rect.Empty;
        var contentBounds = PlaybackBar.Capsule.TransformToVisual(PlaybackBar.CapsuleContent).TransformBounds(PlaybackBar.CapsuleBounds);
        contentBounds.Inflate(.7, .7);
        if (!contentBounds.Contains(stamp) || !contentBounds.Contains(options) || !contentBounds.Contains(transport) ||
            transport.IntersectsWith(options) || stamp.IntersectsWith(transport) ||
            (PlaybackBar.Seek.IsVisible && (progress.IntersectsWith(stamp) || progress.IntersectsWith(transport) || PlaybackBar.Seek.ActualWidth < 319)))
            throw new InvalidOperationException("Playback and shape controls must fit without overlap.");
        var image = new RenderTargetBitmap((int)Root.ActualWidth, (int)Root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        // WPF bitmap capture cannot draw the upstream pixel shader 3.0 path.
        GlassScene.SetRenderMode(PlaybackBar.CapsuleGlass, GlassRenderMode.Fallback);
        GlassScene.SetRenderMode(PlaybackBar.ToolGlass, GlassRenderMode.Fallback);
        try
        {
            Root.InvalidateGlass();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (PlaybackBar.CapsuleGlass.Mode == GlassLensMode.Shader || PlaybackBar.ToolGlass.Mode == GlassLensMode.Shader)
                throw new InvalidOperationException("Bitmap capture must use the rendered software material.");
            image.Render(Root);
        }
        finally
        {
            PlaybackBar.CapsuleGlass.ClearValue(GlassScene.RenderModeProperty);
            PlaybackBar.ToolGlass.ClearValue(GlassScene.RenderModeProperty);
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
