using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiquidGlass.Wpf;
using Cliric.Core;

namespace Cliric.App;

public sealed partial class LibraryView
{
    private async Task VerifyPlayerAsync()
    {
        if (Environment.GetCommandLineArgs().Contains("--lyric-lift-test"))
        {
            await _lyrics.VerifyWindowedLyricLiftAsync();
            _lyrics.Close();
            return;
        }
        int assertions = 0;
        VerifyPlayerTypography();
        assertions++;
        if (_lyrics.Playlist.Count != 0 || TrackList.Items.Count != 0 || _lyrics.TrackIndex != -1 || PlayButton.IsEnabled)
            throw new InvalidOperationException("The standard player must start with an empty queue and disabled play control.");
        _lyrics.ShowLyrics();
        if (LyricsButton.IsEnabled || _lyrics.LyricsShown)
            throw new InvalidOperationException("An empty player must keep the lyrics entry disabled.");
        Click(QueueButton);
        await Idle();
        if (!QueuePopup.IsOpen || !QueueEmpty.IsVisible || !HomePage.IsVisible)
            throw new InvalidOperationException("The empty queue must be reachable from the playback bar without leaving Home.");
        Click(QueueButton);
        await _lyrics.SeedDemoTracksForVerificationAsync();
        RefreshPlayer();
        assertions++;
        if (_lyrics.LyricsShown || TrackList.Items.Count != _lyrics.Playlist.Count || PlaybackBar.MiniCover.ImageSource is null || _lyrics.OwnedWindows.Count != 0)
            throw new InvalidOperationException("The app must open on the library with its song and playlist, in a single window.");
        assertions++;

        var song = _lyrics.CurrentSong;
        var clock = _lyrics.Clock;
        await _lyrics.CheckPlaybackLoadingAsync(() => PlayButton.IsEnabled && HeroPlay.IsEnabled);
        assertions++;
        double start = clock.PositionMs;
        Click(PlayButton);
        await Task.Delay(100);
        RefreshPlayer();
        if (!clock.IsPlaying || clock.PositionMs <= start || AutomationProperties.GetName(PlayButton) != "暂停" || HeroPlayText.Text != "暂停")
            throw new InvalidOperationException("The library must advance the shared clock while the lyrics are hidden.");
        if (Environment.GetCommandLineArgs().Contains("--smtc-test"))
            await VerifyPublishedPlaybackAsync(song.Title, clock.DurationMs);
        assertions++;

        Click(LyricsButton);
        await Idle();
        if (!_lyrics.LyricsShown || _lyrics.CurrentSong != song || _lyrics.Clock != clock || !clock.IsPlaying || Application.Current.Windows.Count != 1)
            throw new InvalidOperationException("Opening lyrics must stay in the same window and keep the active track and clock.");
        assertions++;

        double beforeHide = clock.PositionMs;
        _lyrics.ShowLibrary();
        await Task.Delay(100);
        if (_lyrics.LyricsShown || Visibility != Visibility.Visible || !clock.IsPlaying || clock.PositionMs <= beforeHide)
            throw new InvalidOperationException("Returning to the library must leave playback running.");
        assertions++;
        if (MainWindow.PlayerSeekTarget(60000, 60000, preview: true).HasValue ||
            MainWindow.PlayerSeekTarget(60000, 59200, preview: true).HasValue ||
            MainWindow.PlayerSeekTarget(60000, 59200, preview: false) is not 59080 ||
            MainWindow.PlayerSeekTarget(17000, 60000, preview: true) is not 17000)
            throw new InvalidOperationException("Realtime mpv previews must leave a safe EOF margin and use the live duration.");
        assertions++;

        Click(PlayButton);
        RefreshPlayer();
        string folder = Path.Combine(App.DataFolder, "verification");
        Directory.CreateDirectory(folder);
        var samplePlaylists = Enumerable.Range(1, 6).Select(i =>
            new PlaylistTile(new NetEaseMusic.ClientPlaylist(i, $"示例歌单 {i}", ""), Artwork.Create(i))).ToArray();
        RecommendedPlaylists.ItemsSource = samplePlaylists;
        await Settle();
        CapturePlayer(folder, "player.png");
        var glassMode = PlaybackBar.CapsuleGlass.Mode;
        if (PlaybackBar.Expanded || Progress.IsVisible || Progress.IsTabStop || glassMode != PlaybackBar.ToolGlass.Mode ||
            GlassScene.FindScene(PlaybackBar.CapsuleGlass) != _playbackScene || GlassScene.FindScene(PlaybackBar.ToolGlass) != _playbackScene)
            throw new InvalidOperationException("The library must start with a compact capsule and a shared glass scene.");
        PlaybackBar.ShapeButton.IsChecked = true;
        await Task.Delay(425);
        if (!Progress.IsVisible || !Progress.IsTabStop || !PreviousButton.IsVisible ||
            Progress.ActualWidth < PlaybackCapsule.MinimumSeekWidth - .7)
            throw new InvalidOperationException("The expanded capsule must expose the live seek and previous-track controls.");
        Click(PlaybackBar.SoundTool);
        await Idle();
        double originalSliderVolume = VolumeSlider.Value;
        VolumeSlider.Value = 41;
        if (!SoundSurface.IsVisible || !PlaybackBar.SoundTool.IsChecked.GetValueOrDefault() || !ContainsPopup(SoundSurface) ||
            Math.Abs(_lyrics.Options.Volume - .41) > .001 || SettingsVolume.Value != 41)
            throw new InvalidOperationException("The sound panel must control the current player volume.");
        VolumeSlider.Value = originalSliderVolume;
        Click(PlaybackBar.SoundTool);
        await Settle();
        if (SoundSurface.IsVisible)
            throw new InvalidOperationException("Clicking the sound tool again must close its panel.");
        assertions += 2;
        Click(AccountButton);
        await Idle();
        if (!AccountPopup.IsOpen || !LoginButton.IsVisible || QrPanel.IsVisible || !ContainsPopup(AccountSurface))
            throw new InvalidOperationException("The title bar must open the single account panel without starting QR login.");
        AccountPopup.IsOpen = false;
        assertions++;

        ShowPage(SearchPage);
        await Settle();
        if (SearchPage.Visibility != Visibility.Visible || HomePage.Visibility != Visibility.Collapsed || NavSearch.Tag is not true || !SearchQuery.IsVisible || !AccountButton.IsVisible)
            throw new InvalidOperationException("Search must open as a page inside the library.");
        assertions++;
        CapturePlayer(folder, "player-search.png");
        using var rights = JsonDocument.Parse("""
            { "songs": [ {"id":1,"name":"示例歌曲","dt":238000}, {"id":2,"name":"Another Sample","dt":201000},
              {"id":3,"name":"不可播放的示例","dt":185000}, {"id":4,"name":"待确认的示例","dt":211000} ],
              "rights": { "1":"full", "2":"preview", "3":"unavailable" } }
            """);
        var results = NetEaseMusic.ParseClientTracks(rights.RootElement);
        if (results.Select(track => track.Availability).SequenceEqual(["可播放", "仅试听", "当前授权不可播", "权限未确认"]) is false ||
            !results.Select(track => track.Playable).SequenceEqual([true, false, false, true]))
            throw new InvalidOperationException("NetEase must distinguish full access, preview, unavailability and unknown access.");
        ShowNetEaseTracks(results);
        SearchStatus.Text = "找到 4 首 · 离线示例";
        await Settle();
        if (Descendants(SearchResults).OfType<ListBoxItem>().Count() != 4)
            throw new InvalidOperationException("Search results must render as rows on the search page.");
        await VerifyPlaybackMetadataAsync(results, folder);
        assertions++;
        CapturePlayer(folder, "player-search-results.png");
        SearchResults.ItemsSource = null;
        SearchStatus.Text = "";

        ShowPage(SettingsPage);
        await Settle();
        TypographySection.IsExpanded = true;
        await Idle();
        if (!FontPreview.IsVisible || FontPreview.ActualHeight <= 0 || FontFeatureList.Children.Count != 15)
            throw new InvalidOperationException("Opening the interface font settings must show the live preview and all feature controls.");
        CapturePlayer(folder, "player-typography.png");
        await VerifyLiveTypographySettingsAsync();
        assertions++;
        TypographySection.IsExpanded = false;
        await Idle();
        double volume = _lyrics.Options.Volume;
        SettingsVolumeValue.Text = "37";
        SettingsVolumeValue.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(SettingsVolumeValue), 0, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });
        if (LyricsSettingsHost.Content is not SettingsPanel || Math.Abs(_lyrics.Options.Volume - .37) > .001 || VolumeSlider.Value != 37 ||
            SettingsVolumeValue.Text != "37" || Progress.BorderCover.Margin != Progress.GridBorders.Margin)
            throw new InvalidOperationException("Settings must be a page with live volume and the lyric options.");
        assertions++;
        CapturePlayer(folder, "player-settings.png");
        SettingsVolume.Value = volume * 100;
        _volumeCommit.Stop();
        _lyrics.SetVolume(volume, persist: false);
        Click(QueueButton);
        await Settle();
        if (!QueuePopup.IsOpen || !TrackList.IsVisible || !SettingsPage.IsVisible || QueueEmpty.IsVisible || !ContainsPopup(QueueSurface))
            throw new InvalidOperationException("The playlist popup must leave the current main page visible.");
        assertions++;
        var queueImage = new RenderTargetBitmap((int)QueueSurface.ActualWidth, (int)QueueSurface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        queueImage.Render(QueueSurface);
        File.WriteAllBytes(Path.Combine(folder, "player-songs.png"), Artwork.Encode(queueImage));
        var secondSong = _lyrics.Playlist[1];
        TrackList.SelectedIndex = 1;
        await _lyrics.LoadSongAsync(2);
        RefreshPlayer();
        await Idle();
        if (TrackList.SelectedItem is not TrackRow { Number: 2, IsCurrent: false })
            throw new InvalidOperationException("Advancing playback must keep the selected queue row.");
        var secondRow = (ListBoxItem)TrackList.ItemContainerGenerator.ContainerFromIndex(1);
        if (Descendants(secondRow).OfType<EqualizerBars>().Any(bars => bars.Visibility == Visibility.Visible))
            throw new InvalidOperationException("Selecting an inactive queue row must not show the playback indicator.");
        Click(Descendants(secondRow).OfType<Button>().Single(button => button.Name == "QueueTrackPlay"));
        await Idle();
        if (_lyrics.CurrentSong != secondSong || !_lyrics.Playing)
            throw new InvalidOperationException("The playback queue button must start its own track.");
        Click(QueueButton);
        if (QueuePopup.IsOpen)
            throw new InvalidOperationException("A second click must dismiss the playback queue.");
        assertions++;
        await _lyrics.LoadSongAsync(0);
        RefreshPlayer();
        ShowPage(HomePage);

        bool? originalSignIn = _accountSignedIn;
        _accountSignedIn = false;
        try
        {
            Click(NavLiked);
            await Idle();
            if (!PlaylistPage.IsVisible || NavLiked.Tag is not true || NavHome.Tag is true ||
                TitleText.Text != "我喜欢的音乐" || NavRecent.IsEnabled || NavDownloads.IsEnabled)
                throw new InvalidOperationException("The NetEase sidebar entry must open the existing liked playlist. Reserved entries must stay disabled.");
            assertions++;
        }
        finally
        {
            _accountSignedIn = originalSignIn;
            ShowPage(HomePage, animate: false);
        }
        await VerifyPlaylistCancellationAsync(results[0], results[1]);
        assertions++;
        if (!results[0].Playable || !results[3].Playable)
            throw new InvalidOperationException("The queue fixture must contain two eligible tracks.");
        int oldCount = _lyrics.Playlist.Count;
        var previousSong = _lyrics.CurrentSong;
        _lyrics.QueueNetEase(results[0], next: false);
        var appended = _lyrics.Playlist[^1];
        var queuedTrack = results[3] with { CoverUrl = "https://p1.music.126.net/fixture.png" };
        _lyrics.QueueNetEase(queuedTrack, next: true);
        var inserted = _lyrics.Playlist[1];
        if (_lyrics.Playlist.Count != oldCount + 2 || _lyrics.Playlist[^1] != appended ||
            _lyrics.NextQueueIndex() != 1 || _lyrics.CurrentSong != previousSong || _lyrics.QueueCoverUrl(inserted) != queuedTrack.CoverUrl)
            throw new InvalidOperationException("Appending and playing next must preserve the song, order and unplayed cover URL.");
        await _lyrics.RemoveQueueEntryAsync(inserted);
        await _lyrics.RemoveQueueEntryAsync(appended);
        if (_lyrics.Playlist.Count != oldCount || _lyrics.CurrentSong != previousSong || _lyrics.TrackIndex != 0)
            throw new InvalidOperationException("Removing inactive entries must preserve playback.");
        assertions++;

        bool reducedTitle = _lyrics.Options.ReduceMotion;
        _lyrics.Options.ReduceMotion = false;
        UpdateMotionPreference();
        try
        {
            ShowPage(SettingsPage, animate: false);
            await Idle();
            double titleSize = TitleText.FontSize;
            double startY = PageTitleOffset.Y;
            if (!TitleText.IsVisible || TitleText.Text != "设置" || SettingsTitleSlot.ActualHeight != TitleText.ActualHeight ||
                PageTitleScale.ScaleX != 1 || startY < TitleBar.ActualHeight)
                throw new InvalidOperationException("The single title must start over its reserved space in the page.");
            SettingsPage.ScrollToVerticalOffset(42);
            await Idle();
            double middleY = PageTitleOffset.Y;
            if (SettingsContent.MaxWidth != 760 || !TitleText.IsVisible || TitleText.Opacity != 1 ||
                PageTitleScale.ScaleX <= 14 / titleSize || PageTitleScale.ScaleX >= .96 || middleY >= startY)
                throw new InvalidOperationException("The settings title must shrink progressively inside the narrower content column.");
            CapturePlayer(folder, "player-title-moving.png");
            SettingsPage.ScrollToEnd();
            await Idle();
            if (!TitleText.IsVisible || TitleText.FontSize != titleSize || Math.Abs(PageTitleScale.ScaleX * titleSize - 14) > .001 ||
                PageTitleOffset.Y >= middleY || PageTitleOffset.X != SettingsTitleSlot.TranslatePoint(new Point(), TitleLayer).X)
                throw new InvalidOperationException("The same title must dock at 14 DIP and stay aligned with the settings column.");
            CapturePlayer(folder, "player-title-docked.png");
            SettingsPage.ScrollToVerticalOffset(0);
            await Idle();
            if (!TitleText.IsVisible || PageTitleScale.ScaleX != 1 || Math.Abs(PageTitleOffset.Y - startY) > .01)
                throw new InvalidOperationException("Scrolling back must restore the large title.");

            if (SystemParameters.WheelScrollLines != 0)
            {
                var queue = (TrackRow[])TrackList.ItemsSource;
                TrackList.ItemsSource = Enumerable.Range(0, 16).Select(i => queue[i % queue.Length] with { Number = i + 1 }).ToArray();
                Click(QueueButton);
                await Idle();
                var scroller = QueueScroller ?? throw new InvalidOperationException("Queue must use the smooth scroll viewer.");
                scroller.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
                if (!scroller.IsWheelAnimating || scroller.VerticalOffset != 0)
                    throw new InvalidOperationException("Queue scrolling must animate from its current position.");
                await Settle();
                if (scroller.VerticalOffset <= 0)
                    throw new InvalidOperationException("Queue wheel motion must move the visible list.");
                Click(QueueButton);
                TrackList.ItemsSource = queue;
            }

            var seekScale = Progress.GridBordersScaleTransform;
            Progress.BorderCover.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            if (!seekScale.HasAnimatedProperties || seekScale.ScaleY >= 1.55)
                throw new InvalidOperationException("Seek hover must animate from the current thickness.");
            await Settle();
            if (Math.Abs(seekScale.ScaleY - 1.55) > .001 || Progress.BorderBackground.Opacity <= .5)
                throw new InvalidOperationException("Seek hover must settle on a stronger track.");
            Progress.BorderCover.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            if (!seekScale.HasAnimatedProperties || seekScale.ScaleY <= 1)
                throw new InvalidOperationException("Leaving the seek bar must smoothly reverse its hover.");
            await Settle();
            if (seekScale.ScaleY != 1 || Progress.BorderBackground.Opacity != .5)
                throw new InvalidOperationException("Seek hover must finish at the idle appearance.");
        }
        finally
        {
            _lyrics.Options.ReduceMotion = reducedTitle;
            UpdateMotionPreference();
        }
        ShowPage(HomePage, animate: false);
        assertions++;

        RecommendedPlaylists.ItemsSource = samplePlaylists;
        double width = _lyrics.Width, height = _lyrics.Height;
        foreach (var (w, h, name) in new[] { (820d, 560d, "player-compact.png"), (_lyrics.MinWidth, _lyrics.MinHeight, "player-minimum.png") })
        {
            _lyrics.Width = w;
            _lyrics.Height = h;
            await Settle();
            CapturePlayer(folder, name);
            var capsule = PlaybackBar.Capsule.TransformToVisual(PlaybackBar).TransformBounds(PlaybackBar.CapsuleBounds);
            var tools = PlaybackBar.ToolGroup.TransformToVisual(PlaybackBar).TransformBounds(new Rect(PlaybackBar.ToolGroup.RenderSize));
            var blank = PlaybackBar.InputHitTest(new Point((capsule.Left + capsule.Right) / 2, capsule.Bottom - 3));
            var corner = PlaybackBar.InputHitTest(new Point(capsule.Left + 1, capsule.Top + 1));
            if (!IsCompact || RecommendedPlaylists.ActualHeight < 54 || Progress.IsVisible || Progress.IsTabStop ||
                Math.Abs(capsule.Left + capsule.Right - PlaybackBar.ActualWidth) > 1.5 || capsule.Right > tools.Left - 8 ||
                tools.Right > PlaybackBar.ActualWidth || !QueueButton.IsVisible || !LyricsButton.IsVisible ||
                blank != PlaybackBar.CapsuleHitArea || corner is DependencyObject target && PlaybackBar.Capsule.IsAncestorOf(target))
                throw new InvalidOperationException($"The capsule layout must fit and keep its rounded hit area at {w}×{h}.");
            if (!PlaybackBar.HasSoundTool)
            {
                Click(PlaybackBar.TrackStamp);
                var sound = _shellMenu?.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "声音…"));
                sound?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, sound));
                await Idle();
                if (!SoundSurface.IsVisible || !ContainsPopup(SoundSurface))
                    throw new InvalidOperationException("The narrow capsule must retain its audio settings entry.");
                CloseSound(animate: false);
            }
            Click(AccountButton);
            await Idle();
            bool accountFits = ContainsPopup(AccountSurface);
            Click(QueueButton);
            await Idle();
            bool queueFits = ContainsPopup(QueueSurface);
            Click(QueueButton);
            if (!accountFits || !queueFits)
                throw new InvalidOperationException($"Account and queue panels must fit within the {w}×{h} player window.");
            assertions++;
        }
        _lyrics.Width = width;
        _lyrics.Height = height;
        await Idle();

        int maximizeCaptionHit = _lyrics.GetMaximizeCaptionHit();
        var captionPoint = TitleBar.PointToScreen(new Point(TitleBar.ActualWidth / 2, TitleBar.ActualHeight / 2));
        var accountPoint = AccountButton.PointToScreen(new Point(AccountButton.ActualWidth / 2, AccountButton.ActualHeight / 2));
        int customCaptionHit = _lyrics.GetNativeHit(captionPoint), accountHit = _lyrics.GetNativeHit(accountPoint);
        if (_lyrics.WindowStyle != WindowStyle.SingleBorderWindow ||
            System.Windows.Shell.WindowChrome.GetWindowChrome(_lyrics) is null || maximizeCaptionHit != 9 ||
            customCaptionHit != 2 || accountHit != 1)
            throw new InvalidOperationException($"Expected a draggable custom header, a client account button and a Windows maximize button. Hit tests were {customCaptionHit}, {accountHit} and {maximizeCaptionHit}.");
        SystemCommands.MaximizeWindow(_lyrics);
        await Idle();
        await Task.Delay(180);
        double pageTop = HomePage.TranslatePoint(new Point(), this).Y;
        double titleBottom = TitleBar.TranslatePoint(new Point(0, TitleBar.ActualHeight), this).Y;
        bool insideWorkArea = _lyrics.IsNativeWindowInsideWorkArea();
        int maximizedCaptionHit = _lyrics.GetMaximizeCaptionHit();
        if (_lyrics.WindowState != WindowState.Maximized || _lyrics.ResizeMode == ResizeMode.NoResize || !insideWorkArea || maximizedCaptionHit != 9 ||
            pageTop < titleBottom - .5)
            throw new InvalidOperationException($"Maximized player {ActualWidth}×{ActualHeight}: work area {insideWorkArea}, restore hit {maximizedCaptionHit}, page top {pageTop}, header bottom {titleBottom}.");
        CapturePlayer(folder, "player-maximized.png");
        SystemCommands.RestoreWindow(_lyrics);
        await Idle();
        assertions++;

        bool reduced = _lyrics.Options.ReduceMotion;
        _lyrics.Options.ReduceMotion = false;
        UpdateMotionPreference();
        try
        {
            _lyrics.Height = _lyrics.MinHeight;
            await Idle();
            if (SystemParameters.WheelScrollLines != 0 && HomePage.ScrollableHeight > 1)
            {
                double initialOffset = HomePage.VerticalOffset;
                double end = HomePage.ScrollableHeight;
                for (int i = 0; i < 24; i++)
                    HomePage.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
                if (HomePage.VerticalOffset != initialOffset)
                    throw new InvalidOperationException("Wheel events must defer layout to the render loop.");
                await Task.Delay(460);
                if (Math.Abs(HomePage.VerticalOffset - end) > 1)
                    throw new InvalidOperationException("Rapid wheel deltas must accumulate and come to rest at the end of the page.");
                HomePage.ScrollToHome();
                assertions++;
            }
            _lyrics.Height = height;
            await Idle();
            Progress.IsRealTimeUpdate = false;
            double originalWidth = Progress.GridBorders.ActualWidth;
            Progress.BeginPreview(Progress.BorderCover.ActualWidth + 200);
            if (Progress.CurrentTime != Progress.TotalTime || Progress.ElasticOffset <= 0 || Progress.GridBorders.ActualWidth != originalWidth)
                throw new InvalidOperationException("Dragging beyond the shell seek track must stretch visually without changing its hit range.");
            Progress.CancelPreview();
            await Task.Delay(300);
            if (Progress.Dragging || Progress.ElasticOffset != 0 || Progress.GridBordersScaleTransform.ScaleX != 1)
                throw new InvalidOperationException("The shell seek track must settle at its original width.");
            assertions++;
            await VerifyShellMotionAsync(folder);
            assertions++;
            await _lyrics.VerifyPageBlendAsync(folder);
            assertions++;
            _lyrics.ShowLyrics();
            _lyrics.OpenLyricsSettings();
            await Idle();
            if (_lyrics.LyricsShown || !SettingsPage.IsVisible || _lyrics.LyricsProgress != 0 ||
                !SettingsPage.IsAncestorOf(LyricsSettingsHeading) || SettingsPage.VerticalOffset <= 0)
                throw new InvalidOperationException("Lyrics settings must replace the lyric layer immediately and scroll to its own section.");
            assertions++;
        }
        finally
        {
            _lyrics.Options.ReduceMotion = reduced;
            UpdateMotionPreference();
        }

        Click(LyricsButton);
        await Idle();
        if (!_lyrics.LyricsShown || _lyrics.Clock != clock || clock.IsPlaying || _lyrics.CurrentSong != song)
            throw new InvalidOperationException("Reopening lyrics must retain the same paused track and clock.");
        assertions++;
        File.WriteAllText(Path.Combine(folder, "native-player.json"), JsonSerializer.Serialize(new
        {
            passed = true, assemblySha256 = App.AssemblySha256, assertions, maximizeCaptionHit,
            capsuleRenderMode = glassMode.ToString(),
            scope = "Glass capsule playback and panels, empty startup, native Windows caption, shell motion, and lyric settings hand-off"
        }, LyricsFile.JsonOptions));
        bool client = Environment.GetCommandLineArgs().Contains("--netease-client-test");
        if (client || Environment.GetCommandLineArgs().Contains("--netease-test"))
        {
            _lyrics.ShowLibrary();
            await VerifyNetEaseAsync(client);
            _lyrics.Close();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--queue-test"))
        {
            await VerifyQueueRemovalAsync();
            _lyrics.Close();
            return;
        }
        await _lyrics.RunSelfTestAsync();
    }

    private async Task VerifyQueueRemovalAsync()
    {
        _lyrics.ShowLibrary();
        var active = _lyrics.CurrentSong;
        int index = _lyrics.TrackIndex;
        var successor = _lyrics.Playlist[index + 1];
        await _lyrics.RemoveQueueEntryAsync(active);
        if (!ReferenceEquals(_lyrics.CurrentSong, successor) || _lyrics.Playing)
            throw new InvalidOperationException("Removing a paused song must select its successor without resuming playback.");
        foreach (var song in _lyrics.Playlist.Reverse().ToArray())
            await _lyrics.RemoveQueueEntryAsync(song);
        _lyrics.ShowLyrics();
        if (_lyrics.Playlist.Count != 0 || _lyrics.TrackIndex != -1 || _lyrics.LyricsShown)
            throw new InvalidOperationException("Removing the last song must restore the empty player.");
        var first = new NetEaseTrack("", 95, "第一首", "测试艺人", "测试歌单", 4000, true, ClientSession: true);
        var second = first with { OriginalId = 96, Title = "第二首" };
        var loaded = new Song { Id = "netease-95", Title = first.Title, Artist = first.Artist, DurationMs = 4000 };
        _lyrics.ReplaceNetEaseQueue([first, second, first], 0, loaded);
        if (_lyrics.Playlist.Count != 3 || !ReferenceEquals(_lyrics.Playlist[0], loaded) ||
            _lyrics.Playlist[1].Id != "netease-96" || _lyrics.Playlist[2].Id != "netease-95" ||
            ReferenceEquals(_lyrics.Playlist[0], _lyrics.Playlist[2]))
            throw new InvalidOperationException("Playing a whole list must replace the queue and keep distinct entries in playlist order.");
    }

    private async Task VerifyPlaybackMetadataAsync(NetEaseTrack[] tracks, string folder)
    {
        var original = ReadTypographyControls();
        double width = _lyrics.Width;
        _refresh.Stop();
        try
        {
            ApplyPlayerTypography(new PlayerTypography(490, 0));
            double[] durations = [279_000, 233_000, 268_000, 3_723_000];
            ShowNetEaseTracks(tracks.Select((track, i) => track with { DurationMs = durations[i] }).ToArray());
            _lyrics.Width = 1032;
            await Idle();
            var labels = Descendants(SearchResults).OfType<MiSansLabel>().Where(label => label.Name == "DurationLabel").ToArray();
            if (labels.Length != durations.Length || labels.Any(label => label.TextTrimming != TextTrimming.None || !Fits(label)))
                throw new InvalidOperationException("All track durations must fit at variable MiSans weights, including hour-long tracks.");

            NetEaseQualityButton.Content = QualityName("jymaster");
            NetEaseQualityButton.Visibility = Visibility.Visible;
            ShowSound(PlaybackBar.SoundTool);
            await Idle();
            var quality = Descendants(NetEaseQualityButton).OfType<MiSansLabel>().Single(label => label.Text == "超清母带");
            if (!VolumeSlider.IsVisible || !Fits(quality) || !ContainsPopup(SoundSurface))
                throw new InvalidOperationException("The full quality label must fit the sound panel alongside the volume controls.");
            CapturePlayer(folder, "player-metadata.png");
        }
        finally
        {
            CloseSound(animate: false);
            _lyrics.Width = width;
            ApplyPlayerTypography(original);
            ShowNetEaseTracks(tracks);
            RefreshPlayer();
            _refresh.Start();
        }

        static bool Fits(MiSansLabel label)
        {
            var size = MiSansRenderer.Measure(label.Text, label.FontSize, label.FontWeight.ToOpenTypeWeight(), MiSansTypography.GetFeatures(label),
                new Size(double.PositiveInfinity, double.PositiveInfinity), label.TextWrapping, TextTrimming.None);
            return label.ActualWidth + .01 >= size.Width;
        }
    }

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

    private async Task VerifyLiveTypographySettingsAsync()
    {
        var original = ReadTypographyControls();
        var path = TypographySettingsPath;
        byte[]? backup = File.Exists(path) ? File.ReadAllBytes(path) : null;
        int weight = original.Weight == 441 ? 459 : 441;
        int features = original.Features ^ MiSansTypography.Tabular;
        var tabular = _fontFeatures.Single(check => (int)check.Tag == MiSansTypography.Tabular);

        try
        {
            FontWeightValue.Text = weight.ToString(System.Globalization.CultureInfo.CurrentCulture);
            FontWeightValue.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(FontWeightValue), 0, Key.Enter)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            });
            if (PlaybackBar.CapsuleArtist.FontWeight.ToOpenTypeWeight() != weight || FontPreview.FontWeight.ToOpenTypeWeight() != weight)
                throw new InvalidOperationException("Entering a weight must update the preview and the player immediately.");

            tabular.IsChecked = (features & MiSansTypography.Tabular) != 0;
            Click(tabular);
            if (MiSansTypography.GetFeatures(PlaybackBar.CapsuleArtist) != features || MiSansTypography.GetFeatures(FontPreview) != features)
                throw new InvalidOperationException("Checking a feature must update the preview and the player immediately.");

            await Settle();
            if (ReadTypography() != new PlayerTypography(weight, features))
                throw new InvalidOperationException("The applied player typography must be saved automatically.");
        }
        finally
        {
            _typographyCommit.Stop();
            _pendingTypography = null;
            ApplyPlayerTypography(original);
            tabular.IsChecked = (original.Features & MiSansTypography.Tabular) != 0;
            FontWeightSlider.Value = original.Weight;
            UpdateTypography();
            if (backup is null)
                File.Delete(path);
            else
                File.WriteAllBytes(path, backup);
        }
    }

    private void VerifyPlayerTypography()
    {
        var starting = ReadTypography();
        if (FontFamily.Source != MiSansTypography.Family.Source || FontWeight.ToOpenTypeWeight() != starting.Weight ||
            _fontFeatures.Length != 15)
            throw new InvalidOperationException("The main window must use MiSans VF and offer its fifteen typography controls.");

        var distinctFeatures = new List<string>();
        for (int i = 0; i < MiSansTypography.Features.Length; i++)
        {
            var feature = MiSansTypography.Features[i];
            if (Pixels(feature.Sample, 330, 0).SequenceEqual(Pixels(feature.Sample, 330, 1 << i)))
                throw new InvalidOperationException("MiSans did not change its sample with " + feature.Tag + ".");
            distinctFeatures.Add(feature.Tag);
        }
        if (Pixels("MiSans 音乐", 441, 0).SequenceEqual(Pixels("MiSans 音乐", 459, 0)))
            throw new InvalidOperationException("MiSans in-between weights must render distinct outlines.");

        try
        {
            ApplyPlayerTypography(new PlayerTypography(441, MiSansTypography.Tabular));
            if (PlaybackBar.CapsuleArtist.FontWeight.ToOpenTypeWeight() != 441 || PlaybackBar.CapsuleArtist.FontFamily.Source != MiSansTypography.Family.Source ||
                MiSansTypography.GetFeatures(PlaybackBar.CapsuleArtist) != MiSansTypography.Tabular ||
                Typography.GetNumeralAlignment(SearchQuery) != FontNumeralAlignment.Tabular)
                throw new InvalidOperationException("The font settings must reach main-window labels and the native input controls.");
        }
        finally { ApplyPlayerTypography(starting); }

        var folder = Path.Combine(App.DataFolder, "verification");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "player-typography.json"), JsonSerializer.Serialize(new
        {
            passed = true,
            assemblySha256 = App.AssemblySha256,
            font = MiSansTypography.FamilyName,
            features = distinctFeatures,
            comparedWeights = new[] { 441, 459 }
        }, LyricsFile.JsonOptions));

        static byte[] Pixels(string text, int weight, int features)
        {
            var image = MiSansRenderer.Render(text, 44, weight, features, new Size(520, 72), new DpiScale(1, 1),
                Colors.White, TextWrapping.NoWrap, TextTrimming.None);
            var bytes = new byte[image.PixelWidth * image.PixelHeight * 4];
            image.CopyPixels(bytes, image.PixelWidth * 4, 0);
            return bytes;
        }
    }

    private bool ContainsPopup(FrameworkElement surface)
    {
        if (PresentationSource.FromVisual(surface) is null)
            return false;
        var window = new Rect(PointToScreen(new Point()), PointToScreen(new Point(ActualWidth, ActualHeight)));
        return window.Contains(new Rect(surface.PointToScreen(new Point()),
            surface.PointToScreen(new Point(surface.ActualWidth, surface.ActualHeight))));
    }

    private async Task VerifyPlaylistCancellationAsync(NetEaseTrack oldTrack, NetEaseTrack newTrack)
    {
        ShowPage(PlaylistPage, animate: false, liked: true);
        var pending = new TaskCompletionSource<(NetEaseTrack[], int?)>();
        CancellationToken oldToken = default;
        var old = LoadPlaylistAsync("liked", 0, 0, token =>
        {
            oldToken = token;
            return pending.Task;
        });
        Click(NavLiked);
        await Idle();
        if (oldToken.IsCancellationRequested || PlaylistSpinner.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Re-selecting the open library must retain its current request.");
        ShowPage(SearchPage, animate: false);
        if (!oldToken.IsCancellationRequested || PlaylistSpinner.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Leaving the playlist must cancel its active request.");
        ShowPage(PlaylistPage, animate: false, liked: true);
        var latest = new TaskCompletionSource<(NetEaseTrack[], int?)>();
        var current = LoadPlaylistAsync("liked", 0, 0, _ => latest.Task);
        pending.SetResult(([oldTrack], 25));
        await old;
        if (PlaylistTracks.Items.Count != 0 || PlaylistSpinner.Visibility != Visibility.Visible)
            throw new InvalidOperationException("A cancelled request must never apply rows or clear the next request's loading state.");
        latest.SetResult(([newTrack], null));
        await current;
        if (PlaylistTracks.Items.Count != 1 || ((SearchRow)PlaylistTracks.Items[0]).Track != newTrack || PlaylistSpinner.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Returning to the playlist must display the latest request.");
        var loaded = PlaylistTracks.ItemsSource;
        PlaylistTracks.SelectedIndex = 0;
        Click(NavLiked);
        await Idle();
        if (!ReferenceEquals(loaded, PlaylistTracks.ItemsSource) || PlaylistTracks.SelectedIndex != 0)
            throw new InvalidOperationException("Re-selecting a loaded library must retain its rows and selection.");
        PlaylistTracks.ItemsSource = null;
        ShowPage(HomePage, animate: false);
    }
    private Task Idle() => Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private async Task Settle()
    {
        await Task.Delay(450);
        await Idle();
    }

    private async Task VerifyShellMotionAsync(string folder)
    {
        var accountTrace = await TraceEntranceAsync(AccountSurface, () => Click(AccountButton));
        CheckEntrance(accountTrace, "Account popup", .52, -6);
        if (AccountButton.IsChecked != true || !AccountPopup.IsOpen || AccountSurface.Opacity != 1)
            throw new InvalidOperationException("The account button must keep its checked state while the panel is open.");
        Click(AccountButton);
        await Task.Delay(30);
        if (!AccountPopup.IsOpen || AccountButton.IsChecked == true || AccountSurface.Opacity <= 0)
            throw new InvalidOperationException("A second click must begin dismissal and clear the checked state.");
        var reopenedTrace = await TraceEntranceAsync(AccountSurface, () => Click(AccountButton));
        if (reopenedTrace.Length == 0 || reopenedTrace[^1].Opacity != 1 || !AccountPopup.IsOpen || AccountButton.IsChecked != true)
            throw new InvalidOperationException("Reopening during dismissal must keep the new panel open.");
        var accountCloseTrace = await TraceEntranceAsync(AccountSurface, () => Click(AccountButton));
        if (AccountPopup.IsOpen || AccountButton.IsChecked == true ||
            !accountCloseTrace.Any(frame => frame.Opacity is > .15 and < .9 && frame.TranslateY < -.5))
            throw new InvalidOperationException("Account dismissal must render an intermediate frame and remove the popup.");
        var queueTrace = await TraceEntranceAsync(QueueSurface, () => Click(QueueButton));
        CheckEntrance(queueTrace, "Queue popup", .52, 6);
        Click(QueueButton);
        await Task.Delay(30);
        if (!QueuePopup.IsOpen || QueueButton.IsChecked == true || QueueSurface.IsHitTestVisible)
            throw new InvalidOperationException("Queue dismissal must keep the animation visible and release its input.");
        var reopenedQueue = await TraceEntranceAsync(QueueSurface, () => Click(QueueButton));
        if (!QueuePopup.IsOpen || QueueButton.IsChecked != true || reopenedQueue.LastOrDefault()?.Opacity != 1)
            throw new InvalidOperationException("The queue must reopen from its current position during dismissal.");
        var queueCloseTrace = await TraceEntranceAsync(QueueSurface, () => Click(QueueButton));
        if (QueuePopup.IsOpen || !queueCloseTrace.Any(frame => frame.Opacity is > .15 and < .9 && frame.TranslateY > .5))
            throw new InvalidOperationException("Queue dismissal must animate back toward its trigger and close.");
        ShowPage(HomePage, animate: false);
        var pageTrace = await TraceEntranceAsync(SearchContent, () => ShowPage(SearchPage));
        CheckEntrance(pageTrace, "Lower sidebar page", .86, 8);
        var upwardNavigationTrace = await TraceEntranceAsync(HomeContent, () => ShowPage(HomePage));
        CheckEntrance(upwardNavigationTrace, "Upper sidebar page", .86, -8);
        var playbackTrace = await TraceEntranceAsync(PlayGlyph, () => { Click(PlayButton); RefreshPlayer(); });
        if (!_lyrics.Playing || !playbackTrace.Any(frame => frame.Opacity is > .48 and < .98) || playbackTrace[^1].Opacity != 1)
            throw new InvalidOperationException("The lower playback control must animate its confirmed play-state change.");
        Click(PlayButton);
        RefreshPlayer();

        var target = new TranslateTransform();
        int superseded = 0, completed = 0;
        ShellTransitions.To(target, TranslateTransform.XProperty, 20, MotionTokens.PageEnter, completed: () => superseded++);
        await Task.Delay(50);
        double current = target.X;
        if (current <= 0 || current >= 20)
            throw new InvalidOperationException($"The native clock must reach a real intermediate position before retargeting: {current}.");
        ShellTransitions.To(target, TranslateTransform.XProperty, -10, MotionTokens.PageEnter, completed: () => completed++);
        if (Math.Abs(target.X - current) > .1)
            throw new InvalidOperationException($"Motion must retarget from the current visual position: {current} became {target.X}.");
        await Task.Delay(MotionTokens.PageEnter.Milliseconds + 65);
        if (superseded != 0 || completed != 1 || target.X != -10 || target.HasAnimatedProperties)
            throw new InvalidOperationException($"Superseded completions {superseded}, latest completions {completed}, value {target.X}, animated {target.HasAnimatedProperties}.");

        ShowPage(SettingsPage, animate: false);
        await Idle();
        var toggle = SettingsRealtimeSeek;
        toggle.ApplyTemplate();
        var knob = (FrameworkElement)toggle.Template.FindName("Knob", toggle);
        bool? originalChecked = toggle.IsChecked;
        var switchTrace = await TraceEntranceAsync(knob, () => toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, originalChecked != true));
        if (!switchTrace.Any(frame => frame.TranslateX is > 1 and < 17) ||
            switchTrace[^1].TranslateX != (originalChecked == true ? 0 : 18))
            throw new InvalidOperationException("A visible switch must render an intermediate thumb position before settling.");
        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, originalChecked != true);
        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, originalChecked);
        await Task.Delay(210);
        if (Math.Abs(((TranslateTransform)knob.RenderTransform).X - (originalChecked == true ? 18 : 0)) > .001 ||
            toggle.FocusVisualStyle is null || FrameworkElementAutomationPeer.CreatePeerForElement(toggle)?.GetPattern(PatternInterface.Toggle) is null)
            throw new InvalidOperationException("The switch must settle to the latest state and retain focus and UI Automation support.");

        ShowPage(HomePage, animate: false);
        var progress = Progress;
        progress.SetPosition(110, 86000);
        progress.UpdateLayout();
        var clip = (RectangleGeometry)progress.GridPlayed.Clip;
        double start = clip.Rect.Width;
        var margin = progress.GridPlayed.Margin;
        for (int i = 0; i < 64; i++)
            progress.SetPosition(120 + i * 10, 86000);
        if (clip.Rect.Width <= start || margin != progress.GridPlayed.Margin || !progress.GridBorders.IsArrangeValid)
            throw new InvalidOperationException("Playback ticks must change the fill without invalidating the seek track's arrangement.");
        RefreshPlayer();

        Click(PlaybackBar.TrackStamp);
        await Idle();
        if (_shellMenu is not { IsOpen: true } menu)
            throw new InvalidOperationException("Main-player context menu must open with its normal input target.");
        ShowPage(SettingsPage, animate: false);
        if (menu.IsOpen)
            throw new InvalidOperationException("An old page's popup must not remain open across navigation.");

        var intervals = new List<double>();
        TimeSpan? previous = null;
        long previousTimestamp = 0;
        void Sample(object? _, EventArgs e)
        {
            if (e is not RenderingEventArgs frame || frame.RenderingTime == previous)
                return;
            long timestamp = Stopwatch.GetTimestamp();
            if (previous is not null)
                intervals.Add(Stopwatch.GetElapsedTime(previousTimestamp, timestamp).TotalMilliseconds);
            previousTimestamp = timestamp;
            previous = frame.RenderingTime;
        }
        CompositionTarget.Rendering += Sample;
        try
        {
            for (int i = 0; i < 12; i++)
            {
                ShowPage(i % 2 == 0 ? SearchPage : SettingsPage);
                await Task.Delay(18);
            }
            ShowPage(SearchPage);
            await Task.Delay(MotionTokens.PageEnter.Milliseconds + 65);
        }
        finally { CompositionTarget.Rendering -= Sample; }
        if (!SearchPage.IsVisible || SettingsPage.IsVisible || HomePage.IsVisible || SearchContent.HasAnimatedProperties || SearchContent.Opacity != 1 ||
            SearchContent.RenderTransform is not TranslateTransform { Y: 0, HasAnimatedProperties: false })
            throw new InvalidOperationException("Rapid page changes must leave only the selected page visible with no remaining animation.");
        if (!SearchQuery.Focus() || !SearchQuery.IsKeyboardFocused)
            throw new InvalidOperationException("Keyboard focus must be available immediately after navigation.");

        SearchSpinner.Visibility = Visibility.Visible;
        await Idle();
        if (!((RotateTransform)SearchSpinner.RenderTransform).HasAnimatedProperties)
            throw new InvalidOperationException("A visible busy indicator must reflect active loading when motion is enabled.");
        var noticeTrace = await TraceEntranceAsync(Toast, () =>
        {
            ShowNotice("动效检查");
            UpdateNotice(_lyrics.PlaybackNotice);
        });
        CheckEntrance(noticeTrace, "Notification", .64, 4);
        UpdateNotice("");
        ShowNotice("最终状态");
        UpdateNotice(_lyrics.PlaybackNotice);
        await Task.Delay(225);
        if (!Toast.IsVisible || Toast.Opacity != 1 || Notice.Text != "最终状态")
            throw new InvalidOperationException("A superseded dismissal must not close the current notification.");

        ShowPage(SettingsPage);
        ShowPage(SearchPage);
        _systemAnimationsEnabled = false;
        try
        {
            UpdateMotionPreference();
            if (!MotionReduced || _lyrics.Options.ReduceMotion || SearchContent.Opacity != 1 || SearchContent.HasAnimatedProperties ||
                ((RotateTransform)SearchSpinner.RenderTransform).HasAnimatedProperties || !Progress.ReduceMotion)
                throw new InvalidOperationException("The Windows preference must settle running shell motion without changing the lyrics setting.");
            ShowPage(SettingsPage);
            if (SettingsContent.HasAnimatedProperties || SettingsContent.Opacity != 1)
                throw new InvalidOperationException("Reduced motion must show the new page immediately.");
            var reducedPopupTrace = await TraceEntranceAsync(AccountSurface, () => Click(AccountButton));
            if (reducedPopupTrace.Length == 0 || reducedPopupTrace.Any(frame => frame.Opacity != 1 || frame.TranslateY != 0))
                throw new InvalidOperationException("Reduced motion must show the account panel without movement or dimming.");
            AccountPopup.IsOpen = false;
        }
        finally
        {
            _systemAnimationsEnabled = true;
            SearchSpinner.Visibility = Visibility.Collapsed;
            ShowNotice("");
            UpdateNotice("");
            UpdateMotionPreference();
            ShowPage(HomePage, animate: false);
        }

        intervals.Sort();
        File.WriteAllText(Path.Combine(folder, "native-entrance-frames.json"), JsonSerializer.Serialize(new
        {
            assemblySha256 = App.AssemblySha256,
            account = accountTrace,
            reopenedAccount = reopenedTrace,
            accountClose = accountCloseTrace,
            page = pageTrace,
            upwardNavigation = upwardNavigationTrace,
            playback = playbackTrace,
            toggle = switchTrace,
            notification = noticeTrace
        }, LyricsFile.JsonOptions));
        File.WriteAllText(Path.Combine(folder, "native-shell-motion.json"), JsonSerializer.Serialize(new
        {
            passed = true,
            assemblySha256 = App.AssemblySha256,
            nativeWindow = true,
            frameIntervals = intervals.Count,
            medianFrameIntervalMs = intervals.Count > 0 ? (double?)intervals[intervals.Count / 2] : null,
            p95FrameIntervalMs = intervals.Count > 0 ? (double?)intervals[(int)((intervals.Count - 1) * .95)] : null,
            scope = "WPF rendering callbacks during rapid navigation in the local fixture; no GPU frame or baseline measurement"
        }, LyricsFile.JsonOptions));
    }

    private static void CheckEntrance(EntranceFrame[] samples, string label, double initialOpacity, double initialTravel)
    {
        if (samples.Length == 0 || samples[0].Opacity > initialOpacity ||
            (initialTravel < 0 ? samples[0].TranslateY > initialTravel : samples[0].TranslateY < initialTravel) ||
            samples.Count(frame => frame.Opacity is > .46 and < .98 && Math.Abs(frame.TranslateY) > .5) < 2 ||
            samples[^1].Opacity != 1 || samples[^1].TranslateY != 0)
            throw new InvalidOperationException($"{label} did not render a clear entrance and settle. First {samples.FirstOrDefault()}, last {samples.LastOrDefault()}.");
    }

    private sealed record EntranceFrame(double Milliseconds, double Opacity, double TranslateX, double TranslateY);

    private async Task<EntranceFrame[]> TraceEntranceAsync(FrameworkElement element, Action enter)
    {
        await Idle();
        var samples = new List<EntranceFrame>();
        TimeSpan? lastFrame = null;
        long start = Stopwatch.GetTimestamp();
        void ReadFrame(object? sender, EventArgs e)
        {
            if (e is not RenderingEventArgs frame || frame.RenderingTime == lastFrame ||
                !element.IsVisible || PresentationSource.FromVisual(element) is null)
                return;
            lastFrame = frame.RenderingTime;
            var shift = element.RenderTransform as TranslateTransform;
            samples.Add(new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, element.Opacity,
                shift?.X ?? 0, shift?.Y ?? 0));
        }
        CompositionTarget.Rendering += ReadFrame;
        try
        {
            enter();
            await Task.Delay(310);
        }
        finally { CompositionTarget.Rendering -= ReadFrame; }
        return samples.ToArray();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }

    private void CapturePlayer(string folder, string filename)
    {
        var image = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        // WPF's offscreen bitmap cannot render the live pixel shader.
        GlassScene.SetRenderMode(PlaybackBar, GlassRenderMode.Fallback);
        try
        {
            UpdateLayout();
            image.Render(this);
        }
        finally
        {
            PlaybackBar.ClearValue(GlassScene.RenderModeProperty);
        }
        File.WriteAllBytes(Path.Combine(folder, filename), Artwork.Encode(image));
    }

    private static async Task VerifyPublishedPlaybackAsync(string title, double duration)
    {
        var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!timeout.IsCancellationRequested)
        {
            foreach (var session in manager.GetSessions())
            {
                var properties = await session.TryGetMediaPropertiesAsync();
                if (properties?.Title == title &&
                    session.GetPlaybackInfo()?.PlaybackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
                    Math.Abs(session.GetTimelineProperties().EndTime.TotalMilliseconds - duration) < 50)
                    return;
            }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Windows did not receive the local track title, playback status, and duration.");
    }

    private async Task VerifyNetEaseAsync(bool client)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var tracks = client
            ? await NetEaseMusic.SearchClientAsync("高尚 薛之谦", timeout.Token)
            : await NetEaseMusic.SearchAsync("晴天", timeout.Token);
        long expectedId = client ? 466122271 : 3356343819;
        var track = tracks.FirstOrDefault(item => item.Playable && item.OriginalId == expectedId)
            ?? throw new InvalidOperationException("The NetEase playback fixture is unavailable under the current authorization.");
        if (client && track.ClientRights != "full")
            throw new InvalidOperationException("The NetEase playback fixture must have confirmed full access in search.");
        if (client)
        {
            var account = NetEaseMusic.ReadClientAccountAsync(timeout.Token);
            var daily = NetEaseMusic.ReadClientLibraryAsync("daily", 0, 0, timeout.Token);
            var liked = NetEaseMusic.ReadClientLibraryAsync("liked", 0, 0, timeout.Token);
            var playlists = NetEaseMusic.ReadClientRecommendationsAsync(timeout.Token);
            await Task.WhenAll(account, daily, liked, playlists);
            if (await account is null)
                throw new InvalidOperationException("The NetEase client session could not be confirmed.");
            var library = await playlists;
            var tiles = PlaylistTiles(library);
            if (tiles.Length == 0 || tiles.All(tile => tile.Artwork is null))
                throw new InvalidOperationException("The NetEase session must supply official playlist covers for the home grid.");
            RecommendedPlaylists.ItemsSource = tiles;
            ShowPage(HomePage, animate: false);
            await Idle();
            var button = Descendants(RecommendedPlaylists).OfType<Button>().First(item => item.Tag is NetEaseMusic.ClientPlaylist);
            Click(button);
            await WaitForAsync(() => !_searchBusy);
            if (!PlaylistPage.IsVisible || SearchPage.IsVisible || PlaylistTracks.Items.Count == 0)
                throw new InvalidOperationException("The selected recommendation must open the songs returned by NetEase.");
            CapturePlayer(Path.Combine(App.DataFolder, "verification"), "player-netease-playlist.png");
            ShowPage(HomePage, animate: false);
            await WaitForAsync(() => tiles.Any(tile => tile.Artwork is BitmapImage { IsDownloading: false, PixelWidth: > 1 }));
            await Settle();
            CapturePlayer(Path.Combine(App.DataFolder, "verification"), "player-home-netease.png");
        }
        try
        {
            if (client)
                _lyrics.Options.UseLyricifyParser = true;
            await _lyrics.PlayNetEaseAsync(track, timeout.Token);
            await WaitForAsync(() => _lyrics.Clock.IsPlaying && _lyrics.Clock.PositionMs > 600);
            using var decoder = Process.GetProcessById(await NetEaseMusic.GetPlaybackProcessIdAsync(timeout.Token));
            if (decoder.MainWindowHandle != IntPtr.Zero)
                throw new InvalidOperationException("The NetEase audio decoder opened an independent window.");
            await WaitForAsync(() => _lyrics.TrackArtwork is BitmapImage);
            RefreshPlayer();
            await Idle();
            CapturePlayer(Path.Combine(App.DataFolder, "verification"), "player-netease.png");
            var cover = (BitmapImage)_lyrics.TrackArtwork!;
            string[] coverSurfaces = ["WebImageAlbum", "WebImageAlbumSmall", "WebImageBackground"];
            var lyricImages = WordEdgeChecks.Find(_lyrics).OfType<Reference.ArtworkImage>().ToArray();
            if (cover.PixelWidth == 0 || !ReferenceEquals(PlaybackBar.MiniCover.ImageSource, cover) || !ReferenceEquals(HeroCover.ImageSource, cover) ||
                coverSurfaces.Any(name => !lyricImages.Any(image => image.Name == name && ReferenceEquals(image.Source, cover))))
                throw new InvalidOperationException("The NetEase cover did not reach the library, lyric covers and background.");
            if (_lyrics.CurrentSong.Title != track.Title || _lyrics.CurrentSong.Lines.Count == 0)
                throw new InvalidOperationException("The authorized track and its timed lyrics did not reach the shared player.");
            int wordTimedLines = _lyrics.CurrentSong.Lines.Count(line => line.Words.Count > 0);
            double decodedDuration = _lyrics.Clock.DurationMs;
            if (client && (wordTimedLines == 0 || Math.Abs(decodedDuration - track.DurationMs) > 2500))
                throw new InvalidOperationException("The client track did not provide full-length audio and word-timed lyrics.");
            _lyrics.Seek(28250);
            await WaitForAsync(() => _lyrics.Clock.PositionMs is >= 28250 and < 31000);
            var audio = await NetEaseMusic.ReadPlaybackAsync(timeout.Token);
            if (audio.PositionMs is < 28250 or >= 31000)
                throw new InvalidOperationException("The audio decoder did not seek with the shared clock.");
            _lyrics.TogglePlay();
            await WaitForAsync(() => !_lyrics.Clock.IsPlaying);
            if ((await NetEaseMusic.ReadPlaybackAsync(timeout.Token)).Playing)
                throw new InvalidOperationException("The audio decoder did not pause with the shared clock.");
            string[] levels = client ? await NetEaseMusic.ReadClientQualityAsync(track, timeout.Token) : [];
            if (client && levels.Length == 0)
                throw new InvalidOperationException("The authorized track must have at least one quality reported by the provider.");
            string? switched = levels.FirstOrDefault(level => level != _lyrics.ActualNetEaseQuality);
            if (switched is not null)
            {
                await _lyrics.SwitchNetEaseQualityAsync(track, switched, timeout.Token);
                var changed = await NetEaseMusic.ReadPlaybackAsync(timeout.Token);
                if (_lyrics.ActualNetEaseQuality != switched || changed.Playing || Math.Abs(changed.PositionMs - audio.PositionMs) > 3000)
                    throw new InvalidOperationException("Changing quality must reload the selected audio and preserve pause and position.");
            }
            _lyrics.TogglePlay();
            await WaitForAsync(() => _lyrics.Clock.IsPlaying);
            _lyrics.ShowLyrics();
            await Idle();
            _lyrics.ShowLibrary();
            if (_lyrics.LyricsShown || !_lyrics.Clock.IsPlaying)
                throw new InvalidOperationException("Leaving the lyric page interrupted NetEase playback.");
            await _lyrics.LoadSongAsync(0);
            if (_lyrics.HasNetEasePlayback || _lyrics.PlaybackSource == "网易云音乐")
                throw new InvalidOperationException("Switching sources left NetEase playback active.");
            string report = client ? "native-netease-client.json" : "native-netease.json";
            File.WriteAllText(Path.Combine(App.DataFolder, "verification", report), JsonSerializer.Serialize(new
            {
                passed = true, assemblySha256 = App.AssemblySha256, trackId = track.OriginalId, decodedDurationMs = decodedDuration, wordTimedLines,
                availableQualityCount = levels.Length, qualitySwitchVerified = switched is not null, lyricifyParserSelected = _lyrics.Options.UseLyricifyParser,
                coverWidth = cover.PixelWidth, coverHeight = cover.PixelHeight,
                decoderWindow = false,
                scope = client
                    ? "Confirmed account and search access, daily tracks, playlist recommendations and liked tracks, timed lyrics, artwork and playback controls"
                    : "Authorized search, timed lyrics, album artwork, windowless audio, seek, pause, resume, lyric page round trip, local source switch"
            }, LyricsFile.JsonOptions));
        }
        finally { await _lyrics.StopNetEaseAsync(); }

        async Task WaitForAsync(Func<bool> condition)
        {
            while (!condition())
                await Task.Delay(50, timeout.Token);
        }
    }
}
