using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Windows.Media;
using Cliric.Core;
using Timeline = Cliric.Core.Timeline;
using Cliric.App.Reference;

namespace Cliric.App;

public sealed partial class MainWindow
{
    internal async Task SeedDemoTracksForVerificationAsync()
    {
        _songs.AddRange(DemoSongs.Create());
        await LoadSongAsync(0);
        _clock.Seek(8500);
    }

    internal async Task CheckPlaybackLoadingAsync(Func<bool> controlsEnabled)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = WhilePlaybackLoadsAsync(() => release.Task);
        bool wasPlaying = Playing;
        try
        {
            TogglePlay();
            if (!PlaybackLoading || controlsEnabled() || Playing != wasPlaying)
                throw new InvalidOperationException("Playback controls and shortcuts must be inactive while loading.");
        }
        finally
        {
            release.SetResult();
            await task;
        }
        if (PlaybackLoading || !controlsEnabled())
            throw new InvalidOperationException("Playback controls did not recover after loading.");
    }

    internal async Task RunSelfTestAsync()
    {
        File.Delete(Path.Combine(App.DataFolder, "self-test-failure.txt"));
        try
        {
            _options.Background = BackgroundMode.Legacy;
            _options.ReduceMotion = true;
            await _background.SetModeAsync(_options.Background, _options);
            await Task.Delay(200);
            _clock.Seek(16000);
            _rebuildPending = true;
            await Task.Delay(150);
            int before = _rows.Count;
            if (before == 0)
                throw new Exception("No lyric rows");
            TogglePlay();
            await Task.Delay(100);
            TogglePlay();
            double paused = _clock.PositionMs;
            await Task.Delay(100);
            if (Math.Abs(_clock.PositionMs - paused) > 1)
                throw new Exception("Pause drift");
            Seek(11000);
            await Task.Delay(50);
            if (Math.Abs(_clock.PositionMs - 11000) > 1)
                throw new Exception("Rewind failed");
            Seek(18000);
            Seek(10000);
            if (Math.Abs(_clock.PositionMs - 10000) > 1)
                throw new Exception("Consecutive seek failed");
            await Task.Delay(150);
            string folder = Path.Combine(App.DataFolder, "verification");
            Directory.CreateDirectory(folder);
            Capture(Path.Combine(folder, "landscape.png"));
            Width = 700;
            Height = 1000;
            await Task.Delay(200);
            if (!_portrait)
                throw new Exception("Portrait failed");
            Capture(Path.Combine(folder, "portrait.png"));
            await CheckNativeReflowAsync(folder);
            await CheckNativeWindowAsync(folder);
            await LoadSongAsync(1);
            await Task.Delay(150);
            if (!_rows.Any(r => r.Backgrounds.Count > 0))
                throw new Exception("Background vocals missing");
            await CheckNativeBackgroundsAsync(folder);
            if (_args.Contains("--compare-backgrounds"))
                await CompareIosBackgroundsAsync(folder);
            int videoArgument = Array.IndexOf(_args, "--video-fixture");
            if (videoArgument >= 0 && videoArgument + 1 < _args.Length)
                await CheckNativeArtworkVideoAsync(_args[videoArgument + 1], folder);
            File.WriteAllText(Path.Combine(folder, "self-test.json"), JsonSerializer.Serialize(new
            {
                passed = true,
                assertions = 8,
                state = State()
            }, LyricsFile.JsonOptions));
            Application.Current.Shutdown(0);
        }
        catch (Exception e) { App.Log(e.ToString()); File.WriteAllText(Path.Combine(App.DataFolder, "self-test-failure.txt"), e.ToString()); Application.Current.Shutdown(1); }
    }
    public async Task VerifyOffscreenAsync(string folder)
    {
        if (!_args.Contains("--verify-offscreen"))
            throw new InvalidOperationException("Offscreen verification requires its isolated profile.");
        Content = null;
        Directory.CreateDirectory(folder);
        var checks = new List<string>();
        var layouts = new List<object>();
        try
        {
            SettingsPanelChecks.Run(Check);
            SleepPreventionChecks.Run(Check);
            InteractionFeedbackChecks.Run(Check);
            Check(!_sleepPrevention.IsHeld, "disconnected verification starts without a real system sleep request");
            ReferenceFontChecks.Run(Check);
            MagicStripChecks.Run(Check);
            await MagicStripChecks.RunDynamicAsync(Check);
            foreach (var style in new (int? Preset, double Artwork, double Column, double Artist, double ArtistTop, double TitleBottom)[] { (16, 90, 110, 24.5, 3, -1), (17, 108, 125, 22, 4, -3), (99, 108, 125, 22, 4, -3), (null, 100, 120, 25, 5, 0) })
            {
                _options.AppleMusicLyrics.IosStylePreset = style.Preset;
                _reference.ApplyMobileInfoStyle(style.Preset);
                Check(_smallArtwork.Width == style.Artwork && _smallArtwork.Height == style.Artwork && _reference.Get<ColumnDefinition>("ViewboxSongInfoSmallColumnDefinition1").Width.Value == style.Column, "mobile information layout matches the recovered Loaded branch " + style.Preset);
                Check(_smallArtist.FontSize == style.Artist && _smallArtist.Margin.Top == style.ArtistTop && _reference.Get<Grid>("DockPanelVerticalModeTrackName").Margin.Bottom == style.TitleBottom, "mobile artist metrics and title spacing match the recovered Loaded branch " + style.Preset);
            }
            Check(_smallArtist.ReadLocalValue(TextBlock.FontFamilyProperty) == DependencyProperty.UnsetValue, "returning to default mobile style restores inherited font selection");
            using (var alignmentFixture = JsonDocument.Parse(typeof(MainWindow).Assembly.GetManifestResourceStream("layout-original.json")!))
            {
                Check(alignmentFixture.RootElement.GetProperty("originalStartCalled").GetBoolean(), "breathing alignment fixture calls the original Start method");
                foreach (var item in alignmentFixture.RootElement.GetProperty("cases").EnumerateArray())
                {
                    var settings = new AppleMusicSettings { RightAlignFirst = item.GetProperty("rightFirst").GetBoolean(), BreathingDotsAlignmentLeft = item.GetProperty("forceLeft").GetBoolean() };
                    var alignment = item.GetProperty("alignment");
                    _dots.SetAlignment(alignment.ValueKind == JsonValueKind.Null ? null : Enum.Parse<HorizontalAlignment>(alignment.GetString()!), settings);
                    Check(_dots.HorizontalAlignment == Enum.Parse<HorizontalAlignment>(item.GetProperty("result").GetString()!) && _dots.RenderTransformOrigin == new Point(item.GetProperty("originX").GetDouble(), item.GetProperty("originY").GetDouble()), "breathing alignment and transform origin match original Start " + item);
                }
            }
            WordEdgeChecks.Run(Check, folder);
            LineAnimationChecks.Run(Check);
            BackgroundAnimationChecks.Run(Check);
            await BreathingPlaybackChecks.RunAsync(Check);
            await WordResetChecks.RunAsync(Check);
            ReflowChecks.Run(Check);
            PlusLighterChecks.Run(Check);
            IsolationEffectChecks.Run(Check);
            IsolationPaletteChecks.Run(Check);
            var isolationOptions = new ViewOptions { Background = BackgroundMode.Isolation };
            var isolatedView = new BackgroundView();
            await isolatedView.SetModeAsync(BackgroundMode.Isolation, isolationOptions);
            await isolatedView.SetArtworkAsync(Artwork.Create(0), "palette-test");
            Check(isolatedView.Children.OfType<IsolationBackground>().Count() == 1 && isolatedView.LastError is null, "Isolation setting creates its real renderer and accepts an artwork palette");
            isolatedView.Dispose();
            Check(isolatedView.Children.Count == 0, "Isolation mode releases its detached view on close");
            var staleIsolation = new BackgroundView();
            await staleIsolation.SetModeAsync(BackgroundMode.Isolation, isolationOptions);
            var pendingPalette = staleIsolation.SetArtworkAsync(Artwork.Create(1), "stale-palette");
            staleIsolation.Dispose();
            await pendingPalette;
            Check(staleIsolation.Children.Count == 0, "late palette completion cannot recreate a closed renderer");
            var backgroundOptions = new ViewOptions();
            var backgroundSettings = backgroundOptions.AppleMusicLyrics.BackgroundAppleMusicOriginal;
            backgroundSettings.FrameRateLimit = 48;
            backgroundSettings.RenderScale = .75;
            backgroundSettings.BassPulseScale = 2.3;
            backgroundSettings.BlurScale = .6;
            backgroundSettings.PortraitControlPointCount = 7;
            backgroundSettings.PortraitSubdivisionLevels = 3;
            backgroundSettings.LandscapeControlPointCount = 8;
            backgroundSettings.LandscapeSubdivisionLevels = 4;
            var librarySettings = BackgroundView.GetIosSettings(backgroundOptions);
            Check(librarySettings.FrameRateLimit == 48 && librarySettings.RenderScale == .75 && librarySettings.BlurScale == .6 && librarySettings.PortraitControlPointCount == 7 && librarySettings.PortraitSubdivisionLevels == 3 && librarySettings.LandscapeControlPointCount == 8 && librarySettings.LandscapeSubdivisionLevels == 4, "all original background parameters reach the source renderer settings");
            Check(librarySettings.BassPulseScale == 0, "system-audio pulse remains opt-in");
            backgroundOptions.AudioReactive = true;
            Check(BackgroundView.GetIosSettings(backgroundOptions).BassPulseScale == 2.3, "enabled audio pulse uses the configured scale");
            _reference.UpdateLegacy(0, false, true);
            Check(Math.Abs(_reference.Get<BlurEffect>("WebImageBackgroundBlurEffect").Radius - 160 * VisualTreeHelper.GetDpi(_reference.View).PixelsPerInchX / 192) < .01, "static backdrop uses the loaded reference DPI blur override");
            Check(_reference.Get<Grid>("GridBackgroundSub1").Opacity == 1 && _reference.Get<Grid>("GridBackgroundSub2").Visibility == Visibility.Hidden, "static backdrop uses a single fixed layer");
            _reference.UpdateLegacy(0, false);
            await LoadSongAsync(0);
            using (var waitingSession = new SystemMediaConnection(Dispatcher))
            {
                _system = waitingSession;
                int generation = _loadGeneration;
                try
                {
                    _clock.Play();
                    _clock.Seek(_clock.DurationMs);
                    EndTrack();
                    Check(_loadGeneration == generation, "temporarily missing system media session does not advance the local library");
                    _clock.Pause();
                    _clock.Seek(16000);
                    Seek(1000);
                    Check(_clock.PositionMs == 16000, "missing remote session cannot seek the local lyric clock");
                    TogglePlay();
                    Check(!_clock.IsPlaying, "rejected remote playback cannot start the local clock");
                    Next();
                    Previous();
                    Check(_loadGeneration == generation && _clock.PositionMs == 16000, "missing remote next or previous cannot select or rewind local demo songs");
                    int rejected = 0;
                    foreach (var command in new Func<Task>[] { waitingSession.PlayAsync, waitingSession.PauseAsync, () => waitingSession.SeekAsync(1000), waitingSession.NextAsync, waitingSession.PreviousAsync, () => waitingSession.ShuffleAsync(true), () => waitingSession.RepeatAsync(MediaPlaybackAutoRepeatMode.Track) })
                        try
                        {
                            await command();
                        }
                        catch (InvalidOperationException) { rejected++; }
                    Check(rejected == 7, "all remote transport methods reject a missing session explicitly");
                    bool shuffleBefore = _shuffle, repeatBefore = _repeat;
                    ToggleShuffle();
                    ToggleRepeat();
                    Check(_shuffle == shuffleBefore && _repeat == repeatBefore, "rejected remote shuffle and repeat commands do not toggle local state");
                    double noticeUntil = _noticeUntil;
                    string noticeText = _status.Text;
                    ApplySystemMediaSnapshot(waitingSession, new(_song.Title, "external artist spelling", "", 15000, _clock.DurationMs, false));
                    Check(_title.Text == _song.Title && _artist.Text == _song.Artist && _clock.PositionMs == 15000, "matching media metadata preserves imported title and canonical artist while applying time");
                    Check(!_reference.Get<ImageButton>("ImageClickableShuffleButton").IsEnabled && !_reference.Get<ImageButton>("ImageClickableRepeatButton").IsEnabled, "missing or unsupported remote shuffle and repeat controls stay disabled");
                    ApplySystemMediaSnapshot(waitingSession, new(_song.Title, "external artist spelling", "", 15000, _clock.DurationMs, false, ShuffleActive: true, RepeatMode: MediaPlaybackAutoRepeatMode.Track, CanShuffle: true, CanRepeat: true));
                    Check(_shuffle && _repeat && ReferenceEquals(_reference.Get<ImageButton>("ImageClickableRepeatButton").ImageSource, Application.Current.Resources["Apple.Music.RepeatOneButton"]), "remote playback snapshot controls shuffle highlight and the original repeat-one icon");
                    Check(_noticeUntil == noticeUntil && _status.Text == noticeText, "routine media polling does not replace or perpetually extend a transient notice");
                    ApplySystemMediaSnapshot(waitingSession, new("different song", "different artist", "", 1000, _clock.DurationMs, true));
                    Check(!_clock.IsPlaying && _clock.PositionMs == 15000 && _title.Text == _song.Title, "different media track cannot play stale lyrics or relabel their metadata");
                    var oldCover = _artwork.Source;
                    var mediaCover = Artwork.Encode(Artwork.Create(1));
                    await UpdateMediaArtworkAsync(waitingSession, mediaCover);
                    var refreshedCover = _artwork.Source;
                    Check(!ReferenceEquals(oldCover, refreshedCover) && ReferenceEquals(refreshedCover, _smallArtwork.Source) && ReferenceEquals(refreshedCover, _reference.Get<ArtworkImage>("WebImageBackground").Source), "media artwork reaches main cover, compact cover and bound backdrop");
                    await UpdateMediaArtworkAsync(waitingSession, mediaCover);
                    Check(ReferenceEquals(refreshedCover, _artwork.Source), "polling the same thumbnail does not decode or rebuild the cover repeatedly");
                    using var retiredSession = new SystemMediaConnection(Dispatcher);
                    await UpdateMediaArtworkAsync(retiredSession, Artwork.Encode(Artwork.Create(2)));
                    Check(ReferenceEquals(refreshedCover, _artwork.Source), "retired media connection cannot replace the current artwork");
                }
                finally { _system = null; _clock.Pause(); }
            }
            await MediaArtworkChecks.RunAsync(Check);
            int artworkSongIndex = _songIndex;
            string? importedCoverPath = _song.CoverPath;
            string explicitCoverPath = Path.Combine(folder, "explicit-cover.png");
            File.WriteAllBytes(explicitCoverPath, Artwork.Encode(Artwork.Create(2)));
            try
            {
                _song.CoverPath = explicitCoverPath;
                await LoadSongAsync(artworkSongIndex);
                using var artworkSession = new SystemMediaConnection(Dispatcher);
                _system = artworkSession;
                var explicitCover = _artwork.Source;
                await UpdateMediaArtworkAsync(artworkSession, Artwork.Encode(Artwork.Create(1)));
                Check(ReferenceEquals(explicitCover, _artwork.Source) && ReferenceEquals(explicitCover, _smallArtwork.Source) && ReferenceEquals(explicitCover, _reference.Get<ArtworkImage>("WebImageBackground").Source), "system thumbnails cannot replace an explicitly loaded cover or its backdrop");
            }
            finally { _system = null; _song.CoverPath = importedCoverPath; await LoadSongAsync(artworkSongIndex); }
            foreach (var size in new[] { new Size(1240, 820), new Size(700, 1000), new Size(1600, 900) })
            {
                _root.Width = size.Width;
                _root.Height = size.Height;
                for (int pass = 0; pass < 3; pass++)
                {
                    Layout();
                    RebuildRows();
                    Layout();
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
                _clock.Seek(16000);
                OnRendering(null, EventArgs.Empty);
                Layout();
                Follow(_anchor);
                _scroll.ScrollToVerticalOffset(_scrollTo);
                Layout();
                _autoScrolling = false;
                Check(_rows.Count > 0, "lyrics were populated");
                Check(_portrait == (size.Height / size.Width > .85), "aspect ratio selects layout");
                Check(_scroll.ActualWidth > 100 && _scroll.ViewportHeight > 100, "lyrics viewport has a finite size");
                int gapAnchor = Timeline.Anchor(_groups, 54000 + _options.TimelineOffsetMs);
                Follow(gapAnchor, true);
                double gapTarget = _scrollTo;
                Follow(gapAnchor + 1);
                double nextTarget = _scrollTo;
                double reserved = _portrait ? 83 * _displayScale : 43.5 * _displayScale + _rows[gapAnchor + 1].Host.ActualHeight / 2;
                Check(Math.Abs(nextTarget - gapTarget - reserved) < 1e-6, "interlude scroll uses the next lyric row and the original breathing-space offset");
                var pulled = _rows[gapAnchor + 1].Pull;
                double originalPull = pulled.Y;
                pulled.BeginAnimation(TranslateTransform.YProperty, null);
                pulled.Y = 120;
                Follow(gapAnchor, true);
                Check(Math.Abs(_scrollTo - gapTarget) < 1e-6, "interlude target ignores the current pulling render transform instead of feeding it back into scroll");
                pulled.Y = originalPull;
                Follow(_anchor);
                _scroll.ScrollToVerticalOffset(_scrollTo);
                Layout();
                _autoScrolling = false;
                CheckPageMotion(Check);
                Layout();
                Check(PresentationSource.FromVisual(_root) is null && new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero, "render has no window handle");
                if (!_portrait)
                {
                    var controls = _reference.Get<Grid>("GridHorizontalControls");
                    Check(_seek.TranslatePoint(new Point(), _root).Y < controls.TranslatePoint(new Point(), _root).Y, "progress is above playback controls");
                    Check(Math.Abs(_reference.Get<Grid>("GridArtwork").ActualWidth - 330) < .01, "artwork source size is 330");
                    Check(_reference.Get<Grid>("GridLyricsMaskHost").OpacityMask is null, "landscape has no added vertical fade");
                }
                else
                {
                    Check(_verticalControls.Visibility == Visibility.Hidden && _smallInfo.Visibility == Visibility.Visible, "portrait starts with compact information and hidden controls");
                    SetVerticalControls(true);
                    Layout();
                    Check(_verticalControls.ActualHeight <= size.Height / 3 + .1, "portrait controls occupy at most one third of the page");
                    Check(_reference.Get<Grid>("GridLyricsMaskHost").OpacityMask is LinearGradientBrush fade && fade.GradientStops.Count == 4 && fade.GradientStops[2].Offset < 1, "shown controls fade the lyrics beneath them");
                }
                var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                image.Render(_root);
                string name = $"{size.Width:0}x{size.Height:0}";
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), Artwork.Encode(image));
                layouts.Add(new
                {
                    size,
                    portrait = _portrait,
                    scale = _displayScale,
                    scroll = _scroll.VerticalOffset,
                    target = _scrollTo,
                    viewport = new
                    {
                        width = _scroll.ViewportWidth,
                        height = _scroll.ViewportHeight
                    },
                    artwork = Bounds(_artwork),
                    progress = Bounds(_seek),
                    firstActiveRow = Bounds(_rows[Math.Clamp(_anchor, 0, _rows.Count - 1)].Host)
                });
            }
            var runningBeforeResize = _rows.Select(row => (row.Main.RunningWord, row.Main.FinishingWords)).ToArray();
            _root.Width = 1500;
            Layout();
            RebuildRows();
            Layout();
            Check(_rows.Select(row => (row.Main.RunningWord, row.Main.FinishingWords)).SequenceEqual(runningBeforeResize), "actual page resizing preserves each word worker's selection and finishing state");
            bool initialLineStyle = _options.AppleMusicLyrics.LineSwitchingAppleIosStyle164;
            _options.AppleMusicLyrics.LineSwitchingAppleIosStyle164 = !initialLineStyle;
            RebuildRows();
            Layout();
            Check(_rows.All(row => row.Main.SingOpacityDuration == _singOpacityDuration && row.Backgrounds.All(back => back.SingOpacityDuration == _singOpacityDuration)), "line stop duration keeps the original startup capture when settings rebuild rows");
            _options.AppleMusicLyrics.LineSwitchingAppleIosStyle164 = initialLineStyle;
            RebuildRows();
            Layout();
            var originalFontPreset = _options.AppleMusicLyrics.FontPreset;
            string originalFontFamily = _options.AppleMusicLyrics.FontFamily;
            int? originalMobileStyle = _options.AppleMusicLyrics.IosStylePreset;
            foreach (var preset in new[] { FontPresets.Default, FontPresets.SpotifyMix, FontPresets.SpotifyCircular, FontPresets.SFPro, FontPresets.Custom })
                foreach (int? mobile in new int?[] { null, 16, 17 })
                {
                    _options.AppleMusicLyrics.FontPreset = preset;
                    _options.AppleMusicLyrics.FontFamily = "Consolas";
                    _options.AppleMusicLyrics.IosStylePreset = mobile;
                    _root.Width = 700;
                    _root.Height = 1000;
                    Layout();
                    RebuildRows();
                    Layout();
                    string family = ReferencePageView.FontFor(preset, "Consolas").Source;
                    Check(_reference.View.FontFamily.Source == family && _rows[0].Main.Children.OfType<UserControl>().Single().FontFamily.Source == family, "font preset reaches the actual page and lyric row " + preset + " / " + mobile);
                    Check(_seek.FontFamily.Source == (preset == FontPresets.SFPro ? ReferencePageView.AppleTextFont().Source : family) && _seekVertical.FontFamily.Source == family, "SF Pro horizontal time bar uses Text while the vertical bar inherits the page family " + preset + " / " + mobile);
                    string mobileFont = mobile.HasValue ? ReferencePageView.AppleTextFont().Source : family;
                    Check(_smallTitle.FontFamily.Source == mobileFont && _smallArtist.FontFamily.Source == mobileFont, "mobile layout font override and default inheritance follow original resource 60148 " + preset + " / " + mobile);
                }
            var timeReadouts = new FrameworkElement[]
            {
                _seek.TextBlockCurrentTime, _seek.TextBlockLeftTime, _seek.TimerViewCurrentTime, _seek.TimerViewLeftTime,
                _seekVertical.TextBlockCurrentTime, _seekVertical.TextBlockLeftTime, _seekVertical.TimerViewCurrentTime, _seekVertical.TimerViewLeftTime
            };
            bool originalBold = _options.Bold;
            _options.AppleMusicLyrics.FontPreset = FontPresets.SpotifyCircular;
            TextOptions.SetTextFormattingMode(_reference.View, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(_reference.View, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(_reference.View, TextHintingMode.Fixed);
            foreach (bool bold in new[] { true, false })
            {
                _options.Bold = bold;
                RebuildRows();
                Layout();
                FontWeight weight = bold ? FontWeights.Bold : FontWeights.Medium;
                Check(timeReadouts.All(readout => (FontWeight)readout.GetValue(TextBlock.FontWeightProperty) == weight &&
                    ((FontFamily)readout.GetValue(TextBlock.FontFamilyProperty)).Source == _font.Source &&
                    TextOptions.GetTextFormattingMode(readout) == TextFormattingMode.Ideal &&
                    TextOptions.GetTextRenderingMode(readout) == TextRenderingMode.Auto &&
                    TextOptions.GetTextHintingMode(readout) == TextHintingMode.Auto),
                    "lyric elapsed and remaining time keep the preset typography with a " + weight + " weight inside a display-formatted host");
            }
            _reference.View.ClearValue(TextOptions.TextFormattingModeProperty);
            _reference.View.ClearValue(TextOptions.TextRenderingModeProperty);
            _reference.View.ClearValue(TextOptions.TextHintingModeProperty);
            _options.Bold = originalBold;
            _options.AppleMusicLyrics.FontPreset = originalFontPreset;
            _options.AppleMusicLyrics.FontFamily = originalFontFamily;
            _options.AppleMusicLyrics.IosStylePreset = originalMobileStyle;
            _root.Width = 1600;
            _root.Height = 900;
            Layout();
            RebuildRows();
            Layout();
            var originalLyricsParent = _lyricHost.Parent;
            var originalTrackParent = _reference.Get<Grid>("GridTrackInfo").Parent;
            var originalStageOrder = _stage.Children.Cast<UIElement>().ToArray();
            foreach (var size in new[] { new Size(1100, 713), new Size(700, 1000) })
            {
                _root.Width = size.Width;
                _root.Height = size.Height;
                Layout();
                RebuildRows();
                Layout();
                Rect originalBounds = _lyricHost.TransformToAncestor(_root).TransformBounds(new Rect(_lyricHost.RenderSize));
                foreach (var preset in new[] { PlusLighterEffectPresetTypes.iOS18, PlusLighterEffectPresetTypes.iOS26, PlusLighterEffectPresetTypes.Android })
                {
                    _options.AppleMusicLyrics.EnablePlusLighter = true;
                    _options.AppleMusicLyrics.PlusLighterEffectPreset = preset;
                    for (int pass = 0; pass < 3; pass++)
                    {
                        RebuildRows();
                        Layout();
                        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    }
                    Check(_blend.Active && !ReferenceEquals(_lyricHost.Parent, originalLyricsParent), "blend mode composes the actual lyric tree");
                    Check(_reference.View.IsAncestorOf(_seek) && _reference.View.IsAncestorOf(_lyricHost), "blend reparenting retains controls in the live page tree");
                    Rect blendedBounds = _lyricHost.TransformToAncestor(_root).TransformBounds(new Rect(_lyricHost.RenderSize));
                    Check(Math.Abs(originalBounds.X - blendedBounds.X) < 1 && Math.Abs(originalBounds.Width - blendedBounds.Width) < 1, "blend keeps the lyric viewport position and width");
                    Save($"blend-{(int)size.Width}-{preset}");
                }
                _options.AppleMusicLyrics.EnablePlusLighter = false;
                RebuildRows();
                Layout();
                Check(!_blend.Active && ReferenceEquals(_lyricHost.Parent, originalLyricsParent) && ReferenceEquals(_reference.Get<Grid>("GridTrackInfo").Parent, originalTrackParent), "disabling blend restores original parent slots");
                Check(_stage.Children.Cast<UIElement>().SequenceEqual(originalStageOrder), "blend disable restores exact page sibling ordering");
            }
            _root.Width = 1600;
            _root.Height = 900;
            Layout();
            RebuildRows();
            Layout();
            Seek(6850);
            OnRendering(null, EventArgs.Empty);
            Check(_rows[0].Main.Frame.ContentScale > 1, "timeline compensation applies to the row state before the raw timestamp");
            double preparedStart = _groups[0].StartMs, oldLead = _options.TimelineOffsetMs;
            _options.TimelineOffsetMs = oldLead + 100;
            RebuildRows();
            Check(_groups[0].StartMs == preparedStart, "user lyric lead does not cancel itself inside native row preparation");
            _options.TimelineOffsetMs = oldLead;
            RebuildRows();
            Seek(11000);
            Check(Math.Abs(_clock.PositionMs - 11000) < 1, "rewind uses the absolute clock");
            Seek(18000);
            Seek(10000);
            Check(Math.Abs(_clock.PositionMs - 10000) < 1, "consecutive seeks keep the last position");
            _magicController.Preview(true, MagicAction.Maximize);
            Layout();
            Check(Math.Abs(_reference.Get<Border>("BorderMagicButtonInside").ActualWidth - 30) < .01 && Math.Abs(_reference.Get<Border>("BorderMagicButtonInside").ActualHeight - 30) < .01, "magic strip uses the release hover dimensions");
            Check(_reference.Get<SymbolIcon>("SymbolMagicButton").Text == char.ConvertFromUtf32(62771), "magic strip uses the release Fluent glyph");
            var notice = _reference.Get<Grid>("GridMagicStripFeatureNotice");
            notice.Opacity = 1;
            Layout();
            Check(notice.Children.OfType<Border>().Count(b => b.Visibility == Visibility.Visible) == 1, "magic notice layouts are mutually exclusive");
            Save("magic-hover");
            _magicController.Preview(false, MagicAction.None);
            _clock.Seek(54000);
            OnRendering(null, EventArgs.Empty);
            Layout();
            Check(_dots.Frame.Visible && _dots.Frame.First > _dots.Frame.Second, "breathing dots brighten in order");
            var pausedDots = _dots.Frame;
            await Task.Delay(30);
            OnRendering(null, EventArgs.Empty);
            Check(_dots.Frame == pausedDots, "deterministic breathing preview keeps a fixed song-time sample");
            _scroll.ScrollToVerticalOffset(_scrollTo);
            Layout();
            Save("breathing");
            _clock.Seek(52000);
            OnRendering(null, EventArgs.Empty);
            Check(_dots.Frame.First < pausedDots.First, "rewind reconstructs the earlier breathing frame");
            _clock.Seek(60000);
            OnRendering(null, EventArgs.Empty);
            Check(_dots.Visibility == Visibility.Collapsed, "breathing dots leave when lyrics resume");
            Check(PresentationSource.FromVisual(_root) is null && new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero, "dynamic previews created no window handle");
            await LoadSongAsync(1);
            Layout();
            RebuildRows();
            Layout();
            Check(_rows.Any(r => r.Backgrounds.Count > 0), "background vocals are populated");
            _clock.Seek(21000);
            OnRendering(null, EventArgs.Empty);
            Layout();
            var vocalGroup = _rows.First(r => r.Backgrounds.Count > 0);
            Check(vocalGroup.Backgrounds.All(r => r.Frame.RootOpacity > .6 && r.Frame.RootOpacity < .68), "backing rows use the original opacity target");
            Check(Math.Abs(vocalGroup.Main.Frame.ContentScale - 1.02) < .001, "timed main row uses the original highlight scale");
            Check(vocalGroup.Backgrounds.All(r => vocalGroup.Main.IsAncestorOf(r)), "backing rows are inside the native main text container");
            int vocalIndex = _rows.IndexOf(vocalGroup);
            Follow(vocalIndex, true);
            double beforeBackgroundSpace = _scrollTo;
            var originalMargins = vocalGroup.Backgrounds.Select(back => back.Margin).ToArray();
            for (int i = 0; i < vocalGroup.Backgrounds.Count; i++)
            {
                var back = vocalGroup.Backgrounds[i];
                var margin = back.Margin;
                back.Margin = new Thickness(margin.Left, margin.Top + 17, margin.Right, margin.Bottom);
            }
            Layout();
            Follow(vocalIndex, true);
            Check(Math.Abs(_scrollTo - beforeBackgroundSpace) < 1e-6, "interlude target subtracts the preceding background-vocal layout space");
            for (int i = 0; i < vocalGroup.Backgrounds.Count; i++)
                vocalGroup.Backgrounds[i].Margin = originalMargins[i];
            Layout();
            Follow(_anchor, _breathing);
            _autoScrolling = false;
            double openHeight = vocalGroup.Host.ActualHeight;
            _scroll.ScrollToVerticalOffset(_scrollTo);
            Layout();
            Save("duet-active");
            _clock.Seek(40000);
            OnRendering(null, EventArgs.Empty);
            Layout();
            Check(vocalGroup.Backgrounds.All(r => r.Opacity == 0), "inactive backing rows fade to zero");
            Check(vocalGroup.Host.ActualHeight < openHeight - vocalGroup.Backgrounds.Sum(r => r.NaturalHeight) * .8, "backing rows return their layout space when inactive");
            _clock.Seek(20000);
            OnRendering(null, EventArgs.Empty);
            Layout();
            Check(vocalGroup.Backgrounds.All(r => r.Opacity > .5), "rewind restores backing rows from absolute time");
            Check(Math.Abs(vocalGroup.Host.ActualHeight - openHeight) < 1, "rewind restores the same group height");
            Check(_rows.All(r => r.Pull.Y == 0), "discontinuous seek clears stale pulling transforms");
            double committed = _clock.PositionMs;
            int commits = 0;
            void CountCommit(double _) => commits++;
            _seek.SeekRequested += CountCommit;
            _seek.IsRealTimeUpdate = false;
            _seek.BeginPreview(200);
            Check(_seek.Dragging && commits == 0 && Math.Abs(_clock.PositionMs - committed) < 1, "seek preview does not submit playback");
            _seek.MovePreview(-20);
            Check(_seek.CurrentTime == 0 && _seek.ElasticOffset < 0, "seek left overflow stretches the release bar");
            _seek.CancelPreview();
            Check(!_seek.Dragging && commits == 0 && _seek.ElasticOffset == 0, "seek cancel resets stretch without submitting");
            _seek.BeginPreview(2000);
            Check(_seek.CurrentTime == _seek.TotalTime && _seek.ElasticOffset > 0, "seek right overflow clamps time and stretches");
            _seek.CommitPreview();
            Check(commits == 1 && Math.Abs(_clock.PositionMs - _clock.DurationMs) < 1, "seek release submits once");
            _seek.CommitPreview();
            Check(commits == 1, "idle seek release does not submit");
            _seek.IsRealTimeUpdate = true;
            _seek.BeginPreview(100);
            Check(commits == 2, "real time seek submits during drag");
            _seek.CancelPreview();
            _seek.IsRealTimeUpdate = false;
            _seek.SeekRequested -= CountCommit;
            _seekVertical.BeginPreview(100);
            _seekVertical.CommitPreview();
            Check(Math.Abs(_clock.PositionMs - _seek.CurrentTime) < 1 && _seek.CurrentTime == _seekVertical.CurrentTime, "portrait preview shares time with the primary bar");
            Check(new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero, "seek preview tests created no window");
            var timer = new AnimatedTimerReadout { FontSize = 26, Animate = true };
            timer.Measure(new Size(200, 60));
            timer.Arrange(new Rect(0, 0, 200, 60));
            timer.UpdateLayout();
            timer.Time = TimeSpan.FromSeconds(9);
            timer.Time = TimeSpan.FromSeconds(10);
            Check(timer.Slots[4].Value == '1' && timer.Slots[5].Value == '0' && timer.Slots[5].DelayMs == 110, "rolling time accepts the native second carry animation");
            timer.Time = TimeSpan.FromMilliseconds(-1);
            Check(timer.Slots[0].Value == '-' && timer.Slots[0].Visible, "rolling time accepts the remaining-time sign transition");
            timer.Time = TimeSpan.FromMinutes(120);
            timer.UpdateLayout();
            Check(timer.Text == "120:00", "long tracks retain their full time");
            Check(PresentationSource.FromVisual(timer) is null, "rolling time preview stays disconnected from a window");
            var favorite = new FavoriteButton { Width = 28, Height = 28 };
            var favoriteView = (UserControl)favorite.Content;
            var click = typeof(FavoriteButton).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var animationField = typeof(FavoriteButton).GetField("_favoriteAnimation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            click.Invoke(favorite, null);
            var favoriteAnimation = (Storyboard)animationField.GetValue(favorite)!;
            favoriteAnimation.Pause(favorite);
            favoriteAnimation.SeekAlignedToLastTick(favorite, TimeSpan.FromMilliseconds(125), TimeSeekOrigin.BeginTime);
            Check(favorite.IsFavorited && Math.Abs(((Border)favoriteView.FindName("BorderIcon")).Opacity - .5) < .001 && Math.Abs(((Border)favoriteView.FindName("BorderIconFilled")).Opacity - .3) < .001, "favorite crossfade follows the recovered 100 ms wait and 50 ms fade");
            favoriteAnimation.SeekAlignedToLastTick(favorite, TimeSpan.FromMilliseconds(400), TimeSeekOrigin.BeginTime);
            double favoriteAlpha = ((Border)favoriteView.FindName("BorderIconFilled")).Opacity, favoriteScale = ((ScaleTransform)favoriteView.FindName("BorderIconFilled_ScaleTransform")).ScaleX;
            Check(Math.Abs(favoriteAlpha - .85) < .001 && favoriteScale > 1.37, "favorite reaches its release glow and expansion");
            favoriteAnimation.SeekAlignedToLastTick(favorite, TimeSpan.FromMilliseconds(750), TimeSeekOrigin.BeginTime);
            Check(Math.Abs(((Border)favoriteView.FindName("BorderIconFilled")).Opacity - .6) < .001 && ((DropShadowEffect)favoriteView.FindName("BorderIconFilled_DropShadowEffect")).Opacity < .001, "favorite glow fades during the recovered spring return");
            favoriteAnimation.SeekAlignedToLastTick(favorite, TimeSpan.FromMilliseconds(1100), TimeSeekOrigin.BeginTime);
            Check(((Border)favoriteView.FindName("BorderIconFilled")).Opacity < .001 && Math.Abs(((Border)favoriteView.FindName("BorderIconFilledReversed")).Opacity - .5) < .001 && Math.Abs(((ScaleTransform)favoriteView.FindName("BorderIconFilled_ScaleTransform")).ScaleX - 1) < .001, "favorite finishes in the native reversed-star state");
            click.Invoke(favorite, null);
            click.Invoke(favorite, null);
            favorite.IsFavorited = false;
            Check(animationField.GetValue(favorite) is null && ((Border)favoriteView.FindName("BorderIcon")).Opacity == 1 && ((Border)favoriteView.FindName("BorderIconFilledReversed")).Opacity == 0, "rapid favorite reversal cancels the old animation");
            click.Invoke(favorite, null);
            favorite.ResetVisual();
            Check(animationField.GetValue(favorite) is null && favorite.IsFavorited, "track reload clears animation even when favorite state stays equal");
            click.Invoke(favorite, null);
            click.Invoke(favorite, null);
            favorite.MotionEnabled = false;
            Check(animationField.GetValue(favorite) is null && ((Border)favoriteView.FindName("BorderIconFilledReversed")).Opacity == .5, "reducing motion cancels an in-flight favorite animation");
            favorite.SetPressedVisual(true);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(Math.Abs(((ScaleTransform)favoriteView.FindName("GridMain_ScaleTransform")).ScaleX - .85) < .001, "favorite press uses the native scale with reduced motion");
            favorite.SetPressedVisual(false);
            Check(PresentationSource.FromVisual(favorite) is null, "favorite animation checks stay disconnected from a window");
            _clock.Play();
            OnRendering(null, EventArgs.Empty);
            Check(_artworkScale.ScaleX == 1 && _artworkScale.ScaleY == 1, "playing artwork uses the release full scale");
            _clock.Pause();
            OnRendering(null, EventArgs.Empty);
            Check(_artworkScale.ScaleX == .75 && _artworkScale.ScaleY == .75, "paused artwork uses the release reduced scale");
            _reference.UpdateLegacy(25, false);
            Check(Math.Abs(_reference.Get<ScaleTransform>("GridBackgroundScaleTransform").ScaleX - 2.6) < .001, "legacy source transform reaches its scale turning point");
            Check(Math.Abs(_reference.Get<TranslateTransform>("GridBackgroundTranslateTransform").Y - 170) < .001, "legacy source transform reaches its motion turning point");
            _reference.UpdateLegacy(0, false);
            var nativeMenu = _reference.Get<ContextMenu>("MenuRightClick");
            Check(ReferenceEquals(ContextMenu, nativeMenu) && nativeMenu.Items.OfType<MenuItem>().Take(7).All(m => m.Header is string text && !string.IsNullOrWhiteSpace(text)), "native page menu labels and seven commands are connected");
            var themedMenu = new ContextMenu();
            ReferenceMenus.Apply(themedMenu);
            themedMenu.ApplyTemplate();
            ReferenceMenus.UpdateTheme(true);
            themedMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Check(((SolidColorBrush)themedMenu.Background).Color == Color.FromRgb(243, 243, 243) && ((SolidColorBrush)_reference.Get<ContextMenu>("MenuThreeDot").Background).Color == Color.FromArgb(232, 68, 68, 68), "reopening a menu refreshes its theme while preserving explicit page translucency");
            ReferenceMenus.UpdateTheme(false);
            themedMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Check(((SolidColorBrush)themedMenu.Background).Color == Color.FromRgb(46, 46, 46), "an existing menu resolves the restored dark theme");
            var iconMenuItem = new IconMenuItem { Header = "Menu", SymbolIcon = "Library16", FontSize = 14 };
            iconMenuItem.Measure(new Size(400, 100));
            iconMenuItem.ApplyTemplate();
            Check(iconMenuItem.Template.FindName("Border", iconMenuItem) is Border && ((SymbolIcon)iconMenuItem.Icon).Text == char.ConvertFromUtf32(59233), "derived menu items consume the recovered template and release Library16 glyph");
            iconMenuItem.IsEnabled = false;
            Check(((SolidColorBrush)iconMenuItem.Foreground).Color == Color.FromArgb(93, 255, 255, 255), "disabled menu items consume the source theme foreground");
            double oldZoom = _options.Zoom;
            Check(!HandleKey(Key.OemPlus, ModifierKeys.Control, true) && _options.Zoom == oldZoom, "text editing does not trigger lyric shortcuts");
            Check(HandleKey(Key.OemPlus, ModifierKeys.Control) && _options.Zoom > oldZoom, "native zoom shortcut changes display scale");
            Layout();
            RebuildRows();
            Layout();
            HandleKey(Key.D0, ModifierKeys.Control);
            Layout();
            RebuildRows();
            Layout();
            Check(Math.Abs(_options.Zoom - 1) < .001, "reset zoom restores the current window baseline");
            var originalZoomLevels = _options.AppleMusicLyrics.ZoomingLevels;
            double originalHostZoom = _options.Zoom;
            _options.AppleMusicLyrics.ZoomingLevels = new()
            {
                Horizontal = .8,
                Vertical = 1.3,
                HorizontalFullscreen = .9,
                VerticalFullscreen = 1.1
            };
            foreach (var scene in new (bool Portrait, bool Maximized, double Inverse)[] { (false, false, .8), (true, false, 1.3), (false, true, .9), (true, true, 1.1) })
            {
                _fullscreen = scene.Maximized;
                _root.Width = scene.Portrait ? 700 : 1240;
                _root.Height = scene.Portrait ? 1000 : 820;
                Layout();
                RebuildRows();
                Layout();
                Check(Math.Abs(_displayScale - 1 / scene.Inverse) < 1e-9, "page restores the matching original zoom scene " + scene);
                HandleKey(Key.OemPlus, ModifierKeys.Control);
                Layout();
                RebuildRows();
                Layout();
                double changed = ZoomRules.AdjustInverse(scene.Inverse, true);
                Check(Math.Abs(_displayScale - 1 / changed) < 1e-9 && ZoomRules.Remembered(_options.AppleMusicLyrics.ZoomingLevels, scene.Portrait, scene.Maximized) == changed, "zoom shortcut updates only its active scene " + scene);
                var persisted = SettingsFile.Load(Path.Combine(App.DataFolder, "settings.json"), _ => { });
                Check(ZoomRules.Remembered(persisted.AppleMusicLyrics.ZoomingLevels, scene.Portrait, scene.Maximized) == changed, "zoom shortcut persists its original inverse value " + scene);
            }
            _options.AppleMusicLyrics.ZoomingRestore = false;
            Layout();
            RebuildRows();
            Layout();
            double remembered = _options.AppleMusicLyrics.ZoomingLevels.VerticalFullscreen;
            HandleKey(Key.OemPlus, ModifierKeys.Control);
            Layout();
            RebuildRows();
            Layout();
            Check(_options.AppleMusicLyrics.ZoomingLevels.VerticalFullscreen == remembered && _displayScale > 1, "disabled zoom memory permits a temporary adjustment without overwriting the scene");
            _fullscreen = false;
            _root.Width = 1240;
            _root.Height = 820;
            Layout();
            RebuildRows();
            Layout();
            Check(Math.Abs(_displayScale - 1 / 1.1) < 1e-9, "disabled zoom memory returns to the source default on a scene switch");
            _options.AppleMusicLyrics.ZoomingRestore = true;
            _options.AppleMusicLyrics.ZoomingLevels = new();
            _zoomContext = -1;
            _options.Zoom = 1.2;
            Layout();
            RebuildRows();
            Layout();
            Check(Math.Abs(_displayScale - 1.2 / 1.1) < 1e-9 && Math.Abs(_options.AppleMusicLyrics.ZoomingLevels.Horizontal - 1.1 / 1.2) < 1e-9, "legacy host zoom migrates once into the active original scene");
            _root.Width = 700;
            _root.Height = 630;
            Layout();
            RebuildRows();
            Layout();
            Check(_portrait && Math.Abs(_displayScale - 1 / 1.45) < 1e-9, "another scene starts with its own default after legacy zoom migration");
            HandleKey(Key.D0, ModifierKeys.Control);
            Layout();
            RebuildRows();
            Layout();
            Check(_options.AppleMusicLyrics.ZoomingLevels.Vertical == 1.1 && Math.Abs(_displayScale - 1 / 1.1) < 1e-9, "reset uses the original 1.0 aspect cutoff even within the 0.85 mobile layout");
            _options.AppleMusicLyrics.ZoomingRestore = true;
            _options.AppleMusicLyrics.ZoomingLevels = originalZoomLevels;
            _options.Zoom = originalHostZoom;
            _root.Width = 1600;
            _root.Height = 900;
            Layout();
            RebuildRows();
            Layout();
            bool translation = _options.Translation;
            HandleKey(Key.C, ModifierKeys.Alt);
            RefreshContextMenu();
            Check(_reference.Get<IconMenuItem>("MenuItemTranslation").SymbolIcon == (_options.Translation ? "Checkmark12" : "Empty"), "translation shortcut updates the native menu state");
            HandleKey(Key.C, ModifierKeys.Alt);
            Check(_options.Translation == translation, "translation shortcut is reversible");
            _songs.Add(LyricsFile.ParseLrc("Unscheduled first line\nUnscheduled second line"));
            await LoadSongAsync(_songs.Count - 1);
            for (int pass = 0; pass < 3; pass++)
            {
                Layout();
                RebuildRows();
                Layout();
            }
            _clock.Seek(1000);
            OnRendering(null, EventArgs.Empty);
            Layout();
            Check(_anchor == -1 && _rows.All(r => r.Main.Frame.ContentScale == 1 && Math.Abs(r.Main.Frame.PlayedAlpha - .3) < .001), "plain text remains static without auto highlighting");
            Save("plain-text");
            _songs.Add(new Song { Id = "empty-fixture", Title = "Empty lyrics", Artist = "Lyric Lab", DurationMs = 5000 });
            await LoadSongAsync(_songs.Count - 1);
            for (int pass = 0; pass < 3; pass++)
            {
                Layout();
                RebuildRows();
                Layout();
            }
            OnRendering(null, EventArgs.Empty);
            Layout();
            Check(_rows.Count == 0 && _reference.Get<ColumnDefinition>("ColumnDefinitionRightSide").ActualWidth == 0 && _infoView.Visibility == Visibility.Visible, "empty lyrics collapse the text column");
            var albumBounds = _reference.Get<Grid>("GridArtwork").TransformToAncestor(_root).TransformBounds(new Rect(_reference.Get<Grid>("GridArtwork").RenderSize));
            Check(Math.Abs(albumBounds.X + albumBounds.Width / 2 - _root.Width / 2) < 1, "empty lyrics center the album information");
            Save("empty-lyrics");
            await LoadSongAsync(0);
            for (int pass = 0; pass < 3; pass++)
            {
                Layout();
                OnRendering(null, EventArgs.Empty);
                Layout();
            }
            Check(_rows.Count > 0 && _lyricHost.Visibility == Visibility.Visible && _scroll.ActualWidth > 100, "timed lyrics recover after the empty layout");
            var copyMenu = _rows[0].Main.ContextMenu;
            Check(copyMenu is not null && copyMenu.Items.OfType<MenuItem>().Count() == 7 && copyMenu.Items.OfType<MenuItem>().All(m => m.Header is string text && !string.IsNullOrWhiteSpace(text)), "native line menu has populated copy actions");
            string copyPayload = CopyText.Build(CopyKind.LineWithTranslation, _groups[0].Main, _song.Lines, _groups.Select(g => g.Main));
            Check(copyPayload.Contains(_groups[0].Main.Text, StringComparison.Ordinal) && copyPayload.Contains(_groups[0].Main.Translation, StringComparison.Ordinal), "copy text is prepared without writing the clipboard");
            RefreshTrackMenu();
            Check(_reference.Get<MenuItem>("MenuItemAddToLibrary").Visibility == Visibility.Visible && _reference.Get<MenuItem>("MenuItemDeleteFromLibrary").Visibility == Visibility.Collapsed, "track menu follows local library state");
            Check(_reference.Get<ContextMenu>("MenuArtistNameClick").Items.OfType<MenuItem>().All(m => !m.IsEnabled), "external artist navigation requires source identifiers");
            var sessionField = typeof(BackgroundView).GetField("_session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var held = new HeldBackgroundSession();
            sessionField.SetValue(_background, held);
            _clock.Play();
            var firstNavigation = NavigateLocalAsync(1);
            var secondNavigation = NavigateLocalAsync(2);
            Check(held.Pending.Count == 2, "two navigation artwork operations can overlap");
            held.Complete(1);
            await secondNavigation;
            held.Complete(0);
            await firstNavigation;
            Check(_song.Id == "demo-syllable" && _clock.IsPlaying, "stale navigation cannot pause the current track");
            var thirdNavigation = NavigateLocalAsync(0);
            var fourthNavigation = NavigateLocalAsync(1);
            Check(PlaybackLoading && Playing, "a pending track change retains the existing playback request");
            TogglePlay();
            held.Complete(3);
            await fourthNavigation;
            held.Complete(2);
            await thirdNavigation;
            Check(_song.Id == "demo-duet" && _clock.IsPlaying && !PlaybackLoading, "blocking a toggle during navigation preserves the requested playback and releases the controls");
            sessionField.SetValue(_background, null);
            held.Dispose();
            var closedBackground = new BackgroundView();
            closedBackground.Dispose();
            await closedBackground.SetModeAsync(BackgroundMode.Legacy, _options);
            Check(closedBackground.Children.Count == 0, "disposed background cannot recreate child renderers");
            string assemblyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)));
            File.WriteAllText(Path.Combine(folder, "verification.json"), JsonSerializer.Serialize(new
            {
                passed = true,
                verifiedAtUtc = DateTimeOffset.UtcNow,
                assemblySha256 = assemblyHash,
                assertions = checks.Count,
                checks,
                layouts,
                scope = "Disconnected WPF visual tree; no Window.Show, Application.Run or desktop input; legacy background only."
            }, LyricsFile.JsonOptions));
        }
        finally { _clock.Pause(); _audio.Close(); StopArtworkVideo(); _dots.Dispose(); _magicController.Dispose(); _background.Dispose(); ReleaseSleepRequest(); _disposed = true; }
        void Layout()
        {
            _root.Measure(new Size(_root.Width, _root.Height));
            _root.Arrange(new Rect(0, 0, _root.Width, _root.Height));
            _root.UpdateLayout();
        }
        object Bounds(FrameworkElement element)
        {
            var bounds = element.TransformToAncestor(_root).TransformBounds(new Rect(element.RenderSize));
            return new
            {
                x = bounds.X,
                y = bounds.Y,
                width = bounds.Width,
                height = bounds.Height
            };
        }
        void Save(string name)
        {
            var image = new RenderTargetBitmap((int)_root.Width, (int)_root.Height, 96, 96, PixelFormats.Pbgra32);
            image.Render(_root);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), Artwork.Encode(image));
        }
        void Check(bool value, string label)
        {
            if (!value)
                throw new InvalidOperationException(label);
            checks.Add(label);
        }
    }
    private sealed class HeldBackgroundSession : Lyricify.Backgrounds.IBackgroundSession
    {
        public List<TaskCompletionSource> Pending { get; } = [];
        public bool IsReady => true;
        public event EventHandler FirstFramePresented { add { } remove { } }
        public event EventHandler<Lyricify.Backgrounds.BackgroundFaultedEventArgs> Faulted { add { } remove { } }
        public void UpdateState(Lyricify.Backgrounds.BackgroundState state)
        {
        }
        public Task SetArtworkAsync(Lyricify.Backgrounds.BackgroundArtwork artwork, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(completion);
            return completion.Task;
        }
        public void Complete(int index) => Pending[index].SetResult();
        public void Dispose()
        {
        }
    }
}
