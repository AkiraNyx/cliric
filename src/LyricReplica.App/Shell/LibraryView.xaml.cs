using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LyricReplica.App.Reference;
using LyricReplica.Core;

namespace LyricReplica.App;

public sealed partial class LibraryView : UserControl
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(LibraryView), new PropertyMetadata(false));
    public static readonly DependencyProperty IsNarrowProperty = DependencyProperty.Register(nameof(IsNarrow), typeof(bool), typeof(LibraryView), new PropertyMetadata(false));
    public static readonly DependencyProperty IsPlaybackActiveProperty = DependencyProperty.Register(nameof(IsPlaybackActive), typeof(bool), typeof(LibraryView), new PropertyMetadata(false));
    public static readonly DependencyProperty AlbumColumnProperty = DependencyProperty.Register(nameof(AlbumColumn), typeof(GridLength), typeof(LibraryView), new PropertyMetadata(new GridLength(1, GridUnitType.Star)));
    public static IValueConverter CollapsedWhenTrue { get; } = new FlagVisibility();

    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    public bool IsNarrow { get => (bool)GetValue(IsNarrowProperty); set => SetValue(IsNarrowProperty, value); }
    public bool IsPlaybackActive { get => (bool)GetValue(IsPlaybackActiveProperty); set => SetValue(IsPlaybackActiveProperty, value); }
    public GridLength AlbumColumn { get => (GridLength)GetValue(AlbumColumnProperty); set => SetValue(AlbumColumnProperty, value); }

    private readonly MainWindow _lyrics;
    private readonly bool _selfTest;
    private readonly DispatcherTimer _refresh = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly DispatcherTimer _volumeCommit = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, ImageSource> _artworkCache = [];
    private readonly TranslateTransform _noticeShift = new();
    private int _trackCount = -1, _activeTrack = -1;
    private Song? _displayedSong;
    private ImageSource? _displayedArtwork;
    private bool? _displayedPlaying;
    private bool? _displayedHeart;
    private string _displayedNotice = "";
    private ShellScrollViewer _page;
    private Button _activeNavigation;
    private ContextMenu? _shellMenu;
    private bool _systemAnimationsEnabled;
    private bool _syncingVolume;
    private bool _captionPressed;
    private double _unmutedVolume = .8;
    private string _searchTitle = "搜索", _playlistTitle = "";
    private bool MotionReduced => ShellTransitions.GetReducedMotion(this);
    internal bool IsSeeking => Progress.Dragging;

    private sealed record TrackRow(Song Track, int Number, Lazy<ImageSource?> Thumbnail, string Duration, bool IsCurrent)
    {
        public ImageSource? Artwork => Thumbnail.Value;
    }

    internal LibraryView(MainWindow lyrics, bool selfTest)
    {
        _lyrics = lyrics;
        _selfTest = selfTest;
        ShellMotion.Reduced = () => _lyrics.ReduceMotion;
        _systemAnimationsEnabled = _selfTest || SystemParameters.ClientAreaAnimation;
        FontFamily = MiSansTypography.Family;
        InitializeComponent();
        InitializeTypography();
        SettingsVolume.AttachValueEditor(SettingsVolumeValue, 0);
        _page = HomePage;
        _activeNavigation = NavHome;
        // Leave room inside the Popup HWND for the entrance without moving the resting anchor.
        AccountSurface.Margin = new Thickness(0, MotionTokens.PopupTravel, 0, 0);
        AccountPopup.CustomPopupPlacementCallback = (popup, target, _) => PlaceShellPopup(AccountButton, popup, target, above: false);
        QueuePopup.CustomPopupPlacementCallback = (popup, target, _) => PlaceShellPopup(QueueButton, popup, target, above: true);
        LibraryRoot.IsEnabled = false;
        ShellPlaybackControls.Initialize(PreviousButton, PlayButton, NextButton);
        Progress.UseShellLayout();
        Progress.SeekRequested += position => lyrics.SeekFromPlayer(position, Progress.Dragging);
        _refresh.Tick += (_, _) =>
        {
            UpdateMotionPreference();
            _lyrics.UpdatePlaybackWhileLyricsHidden();
            RefreshPlayer();
        };
        _volumeCommit.Tick += (_, _) => { _volumeCommit.Stop(); _lyrics.SetVolume(VolumeSlider.Value / 100, persist: true); };
        Loaded += OnLoaded;
        SizeChanged += (_, e) =>
        {
            UpdateLayoutMode(e.NewSize.Width);
            CloseShellPopups();
        };
        PreviewMouseDown += OnShellPreviewMouseDown;
        PreviewKeyDown += OnPreviewKeyDown;
        Toast.RenderTransform = _noticeShift;
        UpdateMotionPreference();
        SystemParameters.StaticPropertyChanged += OnSystemMotionChanged;
        _lyrics.StateChanged += (_, _) =>
        {
            UpdateMotionPreference();
            CloseShellPopups();
        };
        _lyrics.LocationChanged += (_, _) => CloseShellPopups();
        _lyrics.Deactivated += (_, _) => CloseShellPopups();
        _lyrics.Closed += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= OnSystemMotionChanged;
            CompositionTarget.Rendering -= PlayAccountEntrance;
            _refresh.Stop();
            _page.StopWheelAnimation();
            _lifetime.Cancel();
            _loginLifetime?.Cancel();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        SetVolumeControls(_lyrics.Options.Volume * 100);
        if (_lyrics.Options.Volume > 0)
            _unmutedVolume = _lyrics.Options.Volume;
        UpdateAccountState();
        PopulateLyricsSettings();
        await _lyrics.StartPlaybackAsync();
        if (!IsVisible && !_lyrics.IsVisible)
            return;
        LibraryRoot.IsEnabled = true;
        RefreshPlayer();
        _refresh.Start();
        if (_selfTest)
            await VerifyPlayerAsync();
    }

    internal void Pause(bool paused)
    {
        if (paused)
        {
            SuspendPlaylistLoad();
            CloseShellPopups();
            _refresh.Stop();
            _page.StopWheelAnimation();
        }
        else if (LibraryRoot.IsEnabled)
        {
            if (_suspendedPlaylistOffset.HasValue)
                Dispatcher.InvokeAsync(ResumePlaylistLoad, DispatcherPriority.Background);
            RefreshPlayer();
            _refresh.Start();
        }
    }

    private void OnSystemMotionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_selfTest || e.PropertyName is not (nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast)))
            return;
        if (Dispatcher.CheckAccess())
            Apply();
        else
            Dispatcher.InvokeAsync(Apply);

        void Apply()
        {
            if (_lifetime.IsCancellationRequested)
                return;
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
            {
                ShellPlaybackControls.ApplyPalette(PreviousButton, PlayButton, NextButton, _lyrics.Playing);
                Progress.ApplyShellPalette();
                return;
            }
            _systemAnimationsEnabled = SystemParameters.ClientAreaAnimation;
            UpdateMotionPreference();
        }
    }

    private void UpdateMotionPreference()
    {
        bool reduced = _lyrics.Options.ReduceMotion || !_systemAnimationsEnabled || _lyrics.WindowState == WindowState.Minimized;
        if (MotionReduced == reduced)
            return;
        ShellTransitions.SetReducedMotion(this, reduced);
        if (reduced)
        {
            _page.FinishWheelAnimation();
            if (QueuePopup.IsOpen)
                QueueScroller?.FinishWheelAnimation();
            ShellTransitions.Settle(Dispatcher);
        }
        Progress.SetShellReducedMotion(reduced);
        UpdatePageTitle();
    }

    internal void UpdatePlaybackAvailability()
    {
        bool loading = _lyrics.PlaybackLoading;
        bool controls = _lyrics.CanControlPlayback;
        PlayButton.IsEnabled = HeroPlay.IsEnabled = (controls || _lyrics.Playlist.Count > 0) && !loading;
        LyricsButton.IsEnabled = _lyrics.CanOpenLyrics;
        LyricsButton.ToolTip = !controls ? "先添加或选择曲目" : loading ? "正在加载曲目…" : "打开 Apple Music 歌词";
        PreviousButton.IsEnabled = NextButton.IsEnabled = controls;
        Progress.IsEnabled = controls && _lyrics.Clock.DurationMs > 0;
        FavoriteButton.IsEnabled = _lyrics.TrackIndex >= 0;
        NetEaseQualityButton.IsEnabled = !loading;
        PlayButton.ToolTip = HeroPlay.ToolTip = loading ? "正在加载音频…" : !controls ? "先添加或选择曲目" : _lyrics.Playing ? "暂停" : "播放";
    }

    private void RefreshPlayer()
    {
        var song = _lyrics.CurrentSong;
        bool trackChanged = !ReferenceEquals(_displayedSong, song);
        var artwork = _lyrics.TrackArtwork;
        bool artworkChanged = !ReferenceEquals(_displayedArtwork, artwork);
        _displayedArtwork = artwork;
        if (artwork is not null && artworkChanged)
            _artworkCache[song.Id] = artwork;

        TrackTitle.Text = HeroTitle.Text = song.Title;
        TrackArtist.Text = HeroArtist.Text = _lyrics.TrackArtist;
        HeroSource.Text = !_lyrics.CanControlPlayback ? "" : string.IsNullOrWhiteSpace(song.Album) ? _lyrics.PlaybackSource : $"{song.Album} · {_lyrics.PlaybackSource}";
        bool qualityVisible = _lyrics.ClientTrack is not null && UseClientSession;
        NetEaseQualityButton.Visibility = qualityVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdatePlaybackSideColumn(qualityVisible);
        NetEaseQualityButton.Content = IsNarrow ? "音质" : QualityName(_lyrics.ActualNetEaseQuality) ?? "音质";
        NetEaseQualityButton.ToolTip = QualityName(_lyrics.ActualNetEaseQuality) is { } actual ? $"当前音质 · {actual}" : "网易云音质";
        if (artworkChanged)
            ShowArtwork(artwork);
        if (trackChanged && _shellMenu?.PlacementTarget == NetEaseQualityButton)
            _shellMenu.IsOpen = false;

        bool playing = _lyrics.Playing;
        IsPlaybackActive = playing;
        HeroEqualizer.IsActive = playing;
        if (_displayedPlaying != playing)
        {
            var glyph = ShellPlaybackControls.SetPlaying(PlayButton, playing, _displayedPlaying.HasValue, MotionReduced);
            _displayedPlaying = playing;
            string label = playing ? "暂停" : "播放";
            HeroPlayGlyph.OpacityMask = new ImageBrush(glyph);
            HeroPlayText.Text = label;
            AutomationProperties.SetName(HeroPlay, label);
        }
        HeroEqualizer.Visibility = HeroLabel.Visibility = _lyrics.CanControlPlayback ? Visibility.Visible : Visibility.Collapsed;
        HeroLabel.Text = playing ? "正在播放" : "已暂停";
        UpdatePlaybackAvailability();

        bool hearted = _lyrics.IsHearted;
        if (_displayedHeart != hearted)
        {
            if (_displayedHeart.HasValue && FavoriteGlyph.IsVisible)
                ShellTransitions.Reveal(FavoriteGlyph, .55, 0, MotionTokens.PlaybackState, MotionReduced);
            _displayedHeart = hearted;
            string label = hearted ? "已收藏" : "收藏";
            FavoriteGlyph.Text = hearted ? "" : "";
            FavoriteGlyph.Foreground = hearted ? (Brush)FindResource("Shell.Accent") : (Brush)FindResource("Shell.Primary");
            FavoriteButton.ToolTip = label;
            AutomationProperties.SetName(FavoriteButton, label);
        }

        ShuffleButton.Tag = _lyrics.ShuffleActive;
        RepeatButton.Tag = _lyrics.RepeatActive;
        AutomationProperties.SetName(ShuffleButton, _lyrics.ShuffleActive ? "随机播放，已开启" : "随机播放");
        AutomationProperties.SetName(RepeatButton, _lyrics.RepeatActive ? "循环播放，已开启" : "循环播放");
        ShuffleButton.IsEnabled = _lyrics.CanShuffle;
        RepeatButton.IsEnabled = _lyrics.CanRepeat;
        UpdateNotice(_lyrics.PlaybackNotice);

        _displayedSong = song;
        bool activeChanged = _activeTrack != _lyrics.TrackIndex;
        if (_trackCount != _lyrics.Playlist.Count || activeChanged || trackChanged || artworkChanged)
        {
            var selectedTrack = (TrackList.SelectedItem as TrackRow)?.Track;
            _trackCount = _lyrics.Playlist.Count;
            PlaylistCount.Text = $"{_trackCount} 首歌曲";
            _activeTrack = _lyrics.TrackIndex;
            var rows = _lyrics.Playlist.Select((track, index) =>
                new TrackRow(track, index + 1, new(() => Thumbnail(track, index)), Duration(track.DurationMs),
                    index == _activeTrack && ReferenceEquals(track, song))).ToArray();
            int selection = Array.FindIndex(rows, row => ReferenceEquals(row.Track, selectedTrack));
            TrackList.ItemsSource = rows;
            TrackList.SelectedIndex = selection >= 0 ? selection : _activeTrack;
        }
        if (trackChanged)
            Progress.CancelPreview();
        Progress.IsRealTimeUpdate = _lyrics.Options.RealtimeSeek;
        if (Progress.AnimatedTime != _lyrics.Options.AnimatedTime)
            Progress.AnimatedTime = _lyrics.Options.AnimatedTime;
        Progress.SetPosition(_lyrics.Clock.PositionMs, _lyrics.Clock.DurationMs);
    }

    private ImageSource? Thumbnail(Song track, int index)
    {
        if (_artworkCache.TryGetValue(track.Id, out var cached))
            return cached;
        if (track.Id.StartsWith("demo-", StringComparison.Ordinal))
            return _artworkCache[track.Id] = Artwork.Create(index);
        return _lyrics.QueueCoverUrl(track) is { } url ? Thumbnail(url) : null;
    }

    private static string Duration(double ms) => ms is > 0 and < 86_400_000
        ? TimeSpan.FromMilliseconds(ms).ToString(ms >= 3_600_000 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture)
        : "";

    private void ShowArtwork(ImageSource? artwork)
    {
        Cover.ImageSource = HeroCover.ImageSource = artwork;
        if (_displayedSong is null || MotionReduced || artwork is null || !CoverFrame.IsVisible)
            ShellTransitions.Set(CoverFrame, OpacityProperty, 1);
        else
            ShellTransitions.Reveal(CoverFrame, CoverFrame.Opacity < .99 ? CoverFrame.Opacity : .76, 0,
                MotionTokens.ArtworkReveal, immediate: false);
    }

    private void UpdateNotice(string text)
    {
        if (text == _displayedNotice)
            return;
        _displayedNotice = text;
        if (text.Length == 0)
        {
            ShellTransitions.To(_noticeShift, TranslateTransform.YProperty, MotionTokens.NoticeTravel, MotionTokens.NoticeExit, MotionReduced);
            ShellTransitions.To(Toast, OpacityProperty, 0, MotionTokens.NoticeExit, MotionReduced,
                completed: () => Toast.Visibility = Visibility.Collapsed);
            return;
        }
        Notice.Text = text;
        if (Toast.Visibility != Visibility.Visible)
        {
            ShellTransitions.Set(_noticeShift, TranslateTransform.YProperty, MotionReduced ? 0 : MotionTokens.NoticeTravel);
            Toast.Visibility = Visibility.Visible;
        }
        ShellTransitions.To(_noticeShift, TranslateTransform.YProperty, 0, MotionTokens.NoticeEnter, MotionReduced);
        ShellTransitions.To(Toast, OpacityProperty, 1, MotionTokens.NoticeEnter, MotionReduced);
    }

    internal void ShowNotice(string text) => _lyrics.Notify(text);

    private void UpdatePlaybackSideColumn(bool qualityVisible)
    {
        PlaybackSideColumn.Width = qualityVisible ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        PlaybackSideColumn.MinWidth = IsCompact ? 132 : 264;
    }

    private Image PlayGlyph => (Image)PlayButton.Content;

    private void UpdateLayoutMode(double width)
    {
        IsCompact = width < 1000;
        IsNarrow = width < 760;
        bool stacked = width < 660;
        SidebarColumn.Width = new GridLength(IsCompact ? 76 : 244);
        AlbumColumn = width < 900 ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        TrackSummaryColumn.MinWidth = stacked ? 0 : 190;
        Grid.SetColumnSpan(TrackDetails, stacked ? 2 : 1);
        Grid.SetRow(TransportArea, stacked ? 1 : 0);
        Grid.SetColumn(TransportArea, stacked ? 0 : 1);
        Grid.SetColumnSpan(TransportArea, stacked ? 3 : 1);
        TransportArea.Margin = stacked ? new Thickness(18, 6, 18, 0) : new Thickness(18, 0, 18, 0);
        double content = Math.Max(0, width - SidebarColumn.Width.Value - 80);
        RecommendedPlaylists.Tag = Math.Clamp((int)(content / 162), 2, 6);
        VolumeSlider.Visibility = width < 1000 ? Visibility.Collapsed : Visibility.Visible;
        HeroArt.Width = HeroArt.Height = width < 600 ? 104 : IsNarrow ? 132 : 184;
        HeroContent.Margin = new Thickness(IsNarrow ? 18 : 22);
        HeroText.Margin = new Thickness(IsNarrow ? 16 : 24, 0, 0, 0);
        Hero.MinHeight = IsNarrow ? 168 : 228;
        HeroTitle.FontSize = IsNarrow ? 24 : 32;
        HeroSource.Visibility = IsNarrow ? Visibility.Collapsed : Visibility.Visible;
        foreach (var page in new[] { HomeContent, SearchContent, PlaylistContent, SettingsContent })
            page.Margin = new Thickness(IsNarrow ? 24 : 40, 16, IsNarrow ? 24 : 40, 36);
        UpdatePageTitle();
    }

    private void OnHeroSizeChanged(object sender, SizeChangedEventArgs e) =>
        Hero.Clip = new RectangleGeometry(new Rect(e.NewSize), 16, 16);

    // Pages

    private void ShowHomePage(object sender, RoutedEventArgs e)
    {
        ShowPage(HomePage);
        _ = LoadHomePlaylistsAsync();
    }
    private void ShowSearchPage(object sender, RoutedEventArgs e)
    {
        if (!_searchBusy && _searchTitle != "搜索")
            ResetNetEaseResults("搜索");
        ShowPage(SearchPage, animate: e is not KeyEventArgs);
        SearchQuery.Focus();
    }
    private void ShowSettingsPage(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);
    internal void ShowLyricsSettings()
    {
        ShowPage(SettingsPage, animate: false);
        ShellTransitions.Reveal((Panel)SettingsPage.Content, 1, 0, default, immediate: true);
        SettingsPage.UpdateLayout();
        double offset = LyricsSettingsHeading.TranslatePoint(new Point(), SettingsContent).Y + SettingsContent.Margin.Top;
        SettingsPage.ScrollToVerticalOffset(offset);
    }

    private void ShowPage(ShellScrollViewer page, bool animate = true, bool liked = false)
    {
        Button navigation = page == SettingsPage ? NavSettings : page == SearchPage ? NavSearch : liked ? NavLiked : NavHome;
        if (page == _page && navigation == _activeNavigation)
            return;
        if (_page == PlaylistPage && page != PlaylistPage)
        {
            CancelPlaylistLoad();
            _playPlaylistRequest?.Cancel();
        }
        double travel = navigation.TranslatePoint(new Point(), Sidebar).Y < _activeNavigation.TranslatePoint(new Point(), Sidebar).Y
            ? -MotionTokens.PageTravel : MotionTokens.PageTravel;
        _activeNavigation.Tag = false;
        _activeNavigation = navigation;
        navigation.Tag = true;
        _page.StopWheelAnimation();
        CloseShellPopups();
        _page.Visibility = Visibility.Collapsed;
        ShellTransitions.Reveal((Panel)_page.Content, 1, 0, default, immediate: true);
        _page = page;
        page.Visibility = Visibility.Visible;
        if (page == SettingsPage)
            SettingsRealtimeSeek.IsChecked = _lyrics.Options.RealtimeSeek;
        var content = (Panel)page.Content;
        ShellTransitions.Reveal(content, MotionTokens.PageStartOpacity, travel, MotionTokens.PageEnter, !animate || MotionReduced);
        TitleText.SetBinding(OpacityProperty, new Binding(nameof(Opacity)) { Source = content });
        BindingOperations.SetBinding(PageTitleEntrance, TranslateTransform.YProperty, new Binding("RenderTransform.Y") { Source = content });
        UpdatePageTitle();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdatePageTitle));
    }

    private void OnPageScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _page) && ReferenceEquals(e.OriginalSource, sender))
            UpdatePageTitle();
    }

    private void OnTitleSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePageTitle();

    private void UpdatePageTitle()
    {
        if (_page is null || TitleLayer is null || TitleDock is null)
            return;
        var (slot, text) = _page == HomePage ? (HomeTitleSlot, "主页") : _page == SearchPage ? (SearchTitleSlot, _searchTitle) :
            _page == PlaylistPage ? (PlaylistTitleSlot, _playlistTitle) : (SettingsTitleSlot, "设置");
        TitleText.Text = text;
        TitleText.TextWrapping = _page == PlaylistPage ? TextWrapping.Wrap : TextWrapping.NoWrap;
        double edge = TitleBar.ActualHeight;
        if (slot.ActualWidth <= 0 || edge <= 0)
        {
            TitleText.Visibility = Visibility.Hidden;
            return;
        }
        TitleText.MaxWidth = slot.ActualWidth;
        double height = TitleText.ActualHeight;
        if (height <= 0)
            return;
        DockedTitleClip.Rect = new Rect(0, 0, TitleDock.ActualWidth, edge);
        BodyTitleClip.Rect = new Rect(0, edge, TitleLayer.ActualWidth, Math.Max(0, TitleLayer.ActualHeight - edge));
        var origin = slot.TranslatePoint(new Point(), TitleLayer);
        if (_page.Content is UIElement { RenderTransform: TranslateTransform shift })
            origin.Y -= shift.Y;

        double compact = Math.Min(14 / TitleText.FontSize, (edge - 10) / height);
        double top = (edge - height * compact) / 2;
        double progress = Math.Clamp((edge - origin.Y) / (2 * (edge - top)), 0, 1);
        if (MotionReduced)
            progress = origin.Y < edge ? 1 : 0;
        double eased = progress * progress * (3 - 2 * progress);
        double left = _page == SettingsPage ? origin.X : IsNarrow ? 24 : 40;
        PageTitleScale.ScaleX = PageTitleScale.ScaleY = 1 + (compact - 1) * eased;
        PageTitleOffset.X = origin.X + (left - origin.X) * eased;
        PageTitleOffset.Y = progress == 0 ? origin.Y : edge + (top - edge) * progress * (2 - progress);
        TitleText.Visibility = Visibility.Visible;
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(_lyrics);
    private void CloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(_lyrics);

    private void ToggleWindowMaximize(object sender, RoutedEventArgs e)
    {
        if (_lyrics.WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(_lyrics);
        else
            SystemCommands.MaximizeWindow(_lyrics);
    }

    internal void UpdateMaximizeCaption(bool maximized)
    {
        MaximizeGlyph.Text = maximized ? "\uE923" : "\uE922";
        AutomationProperties.SetName(MaximizeWindowButton, maximized ? "还原" : "最大化");
    }

    internal bool BeginMaximizePress()
    {
        if (!MaximizeWindowButton.CaptureMouse())
            return false;
        _captionPressed = true;
        ShellTransitions.SetHoverOpacity(MaximizeHover, 1);
        ShellTransitions.SetPressPose(MaximizeGlyph, true);
        return true;
    }

    private bool PointerOverMaximize(MouseEventArgs e) =>
        new Rect(MaximizeWindowButton.RenderSize).Contains(e.GetPosition(MaximizeWindowButton));

    private void UpdateMaximizePress(object sender, MouseEventArgs e)
    {
        if (!_captionPressed)
            return;
        bool over = PointerOverMaximize(e);
        ShellTransitions.SetHoverOpacity(MaximizeHover, over ? 1 : 0);
        ShellTransitions.SetPressPose(MaximizeGlyph, over);
    }

    private void EndMaximizePress(object sender, MouseButtonEventArgs e)
    {
        if (!_captionPressed)
            return;
        e.Handled = true;
        bool over = PointerOverMaximize(e);
        MaximizeWindowButton.ReleaseMouseCapture();
        if (over)
            ToggleWindowMaximize(sender, e);
    }

    private void CancelMaximizePress(object sender, MouseEventArgs e)
    {
        if (!_captionPressed)
            return;
        _captionPressed = false;
        ShellTransitions.SetHoverOpacity(MaximizeHover, 0);
        ShellTransitions.SetPressPose(MaximizeGlyph, false);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_page.IsKeyboardFocusWithin && e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            _page.StopWheelAnimation();
        if (e.Key == Key.Escape && _accountExpanded)
            OnAccountKeyDown(sender, e);
        else if (e.Key == Key.Escape && QueuePopup.IsOpen)
            OnQueueKeyDown(sender, e);
        else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            if (IsCompact)
                ShowSearchPage(this, e);
            else
                SidebarSearch.Focus();
        }
    }

    // Lyrics and track list

    private void OpenLyricsPage(object sender, RoutedEventArgs e) => _lyrics.ShowLyrics();

    private async void PlaySelectedTrack(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(TrackList, source) is not ListBoxItem { DataContext: TrackRow row } container)
            return;
        for (DependencyObject? element = source; element is not null && element != container; element = VisualTreeHelper.GetParent(element))
            if (element is ButtonBase)
                return;
        await SwitchTrackAsync(row);
    }

    private async void PlayQueueTrack(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TrackRow row })
            await SwitchTrackAsync(row);
    }

    private async void RemoveQueueTrack(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TrackRow row })
            return;
        try { await _lyrics.RemoveQueueEntryAsync(row.Track); }
        catch (Exception error) { ShowNotice(error.Message); }
        RefreshPlayer();
    }

    private async void OnTrackListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Handled)
            return;
        var row = e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(TrackList, source) is ListBoxItem { DataContext: TrackRow focused }
            ? focused : TrackList.SelectedItem as TrackRow;
        if (row is null)
            return;
        e.Handled = true;
        await SwitchTrackAsync(row);
    }

    private async Task SwitchTrackAsync(TrackRow row)
    {
        int index = row.Number - 1;
        if (index < 0 || index >= _lyrics.Playlist.Count || !ReferenceEquals(row.Track, _lyrics.Playlist[index]))
            return;
        try
        {
            TrackList.SelectedItem = row;
            if (_lyrics.TrackIndex == index && ReferenceEquals(_lyrics.CurrentSong, row.Track))
            {
                if (!_lyrics.Playing)
                    _lyrics.TogglePlay();
                return;
            }
            await _lyrics.NavigateLocalAsync(index, startPlaying: true);
            RefreshPlayer();
        }
        catch (Exception exception) { ShowNotice(exception.Message); }
    }

    private void OpenTrackActions(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Top, VerticalOffset = -6 };
        ReferenceMenus.Apply(menu);
        Add("为当前曲目关联音频…", () => _lyrics.OpenAudio(_lyrics));
        Add("更换封面…", () => _lyrics.OpenCover(_lyrics));
        menu.Items.Add(new Separator());
        Add("歌词设置", () => ShowPage(SettingsPage));
        OpenShellMenu(menu);
        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
    }

    private void OpenShellMenu(ContextMenu menu)
    {
        if (_shellMenu is { } previous)
            previous.IsOpen = false;
        _shellMenu = menu;
        menu.SetValue(Popup.PopupAnimationProperty, PopupAnimation.None);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_shellMenu, menu))
                _shellMenu = null;
        };
        menu.IsOpen = true;
    }

    private void CloseShellPopups()
    {
        CloseAccount(animate: false);
        QueuePopup.IsOpen = false;
        if (_shellMenu is { } menu)
            menu.IsOpen = false;
    }

    private CustomPopupPlacement[] PlaceShellPopup(FrameworkElement anchor, Size popup, Size target, bool above)
    {
        // WPF passes popup and target sizes in screen pixels to the placement callback.
        var origin = PointToScreen(new Point());
        var end = PointToScreen(new Point(ActualWidth, ActualHeight));
        var position = anchor.PointToScreen(new Point());
        var dpi = VisualTreeHelper.GetDpi(this);
        double left = origin.X + 8 * dpi.DpiScaleX, top = origin.Y + 8 * dpi.DpiScaleY;
        double x = Math.Clamp(position.X + target.Width - popup.Width, left, Math.Max(left, end.X - 8 * dpi.DpiScaleX - popup.Width));
        double desiredY = above ? position.Y - popup.Height - 6 * dpi.DpiScaleY :
            position.Y + target.Height + (6 - MotionTokens.PopupTravel) * dpi.DpiScaleY;
        double y = Math.Clamp(desiredY, top, Math.Max(top, end.Y - 8 * dpi.DpiScaleY - popup.Height));
        return [new CustomPopupPlacement(new Point(x - position.X, y - position.Y), PopupPrimaryAxis.None)];
    }

    private void OnShellPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_accountExpanded && !AccountButton.IsMouseOver && !AccountSurface.IsMouseOver)
            CloseAccount(animate: true);
        if (QueuePopup.IsOpen && !QueueButton.IsMouseOver && !QueueSurface.IsMouseOver)
            QueuePopup.IsOpen = false;
    }

    private void ToggleQueuePopup(object sender, RoutedEventArgs e)
    {
        if (QueuePopup.IsOpen)
        {
            QueuePopup.IsOpen = false;
            return;
        }
        CloseAccount(animate: false);
        ShellTransitions.PrepareReveal(QueueSurface, MotionReduced ? 1 : .45, 0);
        QueueButton.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        QueuePopup.IsOpen = true;
    }

    private void OnQueuePopupOpened(object? sender, EventArgs e)
    {
        if (QueuePopup.IsOpen)
            ShellTransitions.PlayReveal(QueueSurface, MotionTokens.PopupEnter, MotionReduced);
    }

    private void OnQueuePopupClosed(object? sender, EventArgs e)
    {
        if (!QueuePopup.IsOpen)
        {
            QueueScroller?.StopWheelAnimation();
            QueueButton.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            ShellTransitions.PrepareReveal(QueueSurface, 1, 0);
        }
    }

    private ShellScrollViewer? QueueScroller => TrackList.Template.FindName("QueueScroller", TrackList) as ShellScrollViewer;

    private void OnQueueKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        QueuePopup.IsOpen = false;
        QueueButton.Focus();
        e.Handled = true;
    }

    private void OpenAudio(object sender, RoutedEventArgs e) => _lyrics.OpenAudio(_lyrics, newTrack: true);
    private void ImportLyrics(object sender, RoutedEventArgs e) => _lyrics.OpenLyrics(_lyrics);
    private void ConnectMedia(object sender, RoutedEventArgs e) => _lyrics.ConnectSystemMedia();
    private void PreviousTrack(object sender, RoutedEventArgs e) => _lyrics.Previous();
    private void TogglePlayback(object sender, RoutedEventArgs e) => _lyrics.TogglePlay();
    private void NextTrack(object sender, RoutedEventArgs e) => _lyrics.Next();
    private void ToggleShuffle(object sender, RoutedEventArgs e) => _lyrics.ToggleShuffle();
    private void ToggleRepeat(object sender, RoutedEventArgs e) => _lyrics.ToggleRepeat();
    private void ToggleFavorite(object sender, RoutedEventArgs e) => _lyrics.Favorite();

    // Volume

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ChangeVolume(e.NewValue);

    private void ChangeVolume(double percent)
    {
        if (_syncingVolume)
            return;
        SetVolumeControls(percent);
        if (percent > 0)
            _unmutedVolume = percent / 100;
        _lyrics.SetVolume(percent / 100, persist: false);
        _volumeCommit.Stop();
        _volumeCommit.Start();
    }

    private void ToggleMute(object sender, RoutedEventArgs e) =>
        ChangeVolume(VolumeSlider.Value > 0 ? 0 : Math.Max(.05, _unmutedVolume) * 100);

    private void SetVolumeControls(double percent)
    {
        _syncingVolume = true;
        try
        {
            VolumeSlider.Value = SettingsVolume.Value = Math.Round(percent);
            VolumeGlyph.Text = percent <= 0 ? "" : percent < 34 ? "" : percent < 67 ? "" : "";
            AutomationProperties.SetName(MuteButton, percent <= 0 ? "取消静音" : "静音");
            MuteButton.ToolTip = percent <= 0 ? "取消静音" : "静音";
        }
        finally { _syncingVolume = false; }
    }

    // Settings

    private void OnRealtimeSeekChanged(object sender, RoutedEventArgs e) =>
        _lyrics.ApplyPlayerSettings(_lyrics.Options.Volume, SettingsRealtimeSeek.IsChecked == true);

    private void PopulateLyricsSettings()
    {
        var panel = _lyrics.CreateLyricsSettings(PopulateLyricsSettings);
        // The card already names the section; its own headings become quieter group labels.
        foreach (var heading in panel.Children.OfType<TextBlock>().Where(text => text.FontSize >= 19))
        {
            heading.FontSize = 13;
            heading.FontWeight = FontWeights.SemiBold;
            heading.Foreground = (Brush)FindResource("Shell.Secondary");
            heading.Margin = new Thickness(0, 22, 0, 6);
        }
        if (panel.Children.OfType<TextBlock>().FirstOrDefault() is { Text: "歌词设置" } title)
            title.Visibility = Visibility.Collapsed;
        if (panel.Children.OfType<CheckBox>().FirstOrDefault(box => box.Name == "HostReduceMotion") is { } motion)
            motion.Click += (_, _) => UpdateMotionPreference();
        LyricsSettingsHost.Content = panel;
        UpdateMotionPreference();
    }

    private sealed class FlagVisibility : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
