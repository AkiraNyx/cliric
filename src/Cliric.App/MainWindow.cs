using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;
using Windows.Media;
using Cliric.Core;
using Timeline = Cliric.Core.Timeline;
using Cliric.App.Reference;

namespace Cliric.App;

public sealed partial class MainWindow : Window
{
    public static readonly DependencyProperty IsHeartedProperty = DependencyProperty.Register(nameof(IsHearted), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool IsHearted
    {
        get => (bool)GetValue(IsHeartedProperty); set => SetValue(IsHeartedProperty, value);
    }
    private sealed record RowSlot(DisplayLine Data, StackPanel Host, LyricRow Main, IReadOnlyList<LyricRow> Backgrounds)
    {
        public TranslateTransform Pull { get; } = new();
    }
    private ViewOptions _options = App.LoadOptions();
    private readonly PlaybackClock _clock = new();
    private readonly MediaPlayer _audio = new();
    private readonly BackgroundView _background = new();
    private readonly SleepPrevention _sleepPrevention;
    private readonly Grid _root = new();
    private Grid _stage = new(), _lyricHost = new();
    private ReferencePageView _reference = null!;
    private PlusLighterComposer _blend = null!;
    private double _displayScale = 1;
    private double _inverseZoom = 1.1, _rememberedZoom = double.NaN;
    private int _zoomContext = -1;
    private AppleMusicSettings? _zoomSettings;
    private ZoomLevels? _zoomLevels;
    private bool _zoomRestore;
    private readonly ScaleTransform _artworkScale = new(.75, .75);
    private bool? _coverPlaying;
    private double _legacyTime;
    private ColumnDefinition _leftColumn = new() { Width = new GridLength(.82, GridUnitType.Star) };
    private TouchScrollViewer _scroll = null!;
    private Panel _linesPanel = new StackPanel();
    private readonly List<RowSlot> _rows = [];
    private Viewbox _infoView = new() { Stretch = Stretch.Uniform, Margin = new Thickness(0, 70, 0, 60) };
    private FrameworkElement _smallInfo = new Grid();
    private Grid _verticalControls = new();
    private Image _artwork = new() { Stretch = Stretch.UniformToFill }, _smallArtwork = new() { Stretch = Stretch.UniformToFill };
    private TextBlock _title = new(), _artist = new();
    private TextBlock _smallTitle = new(), _smallArtist = new(), _status = new(), _fps = new();
    private BreathingDots _dots = new();
    private ReferenceStateBar _seek = new(), _seekVertical = new();
    private readonly Border _queue = new();
    private FrameworkElement _magic = new Border();
    private UserControl? _credit;
    private MagicStripController _magicController = null!;
    private readonly List<ButtonDragFeedback> _buttonDrags = [];
    private readonly StackPanel _queueItems = new();
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private readonly string[] _args;
    private bool _fileDialogOpen;
    private double _ignoreDialogMouseUntil;
    private readonly List<Song> _songs;
    private List<DisplayLine> _groups = [];
    private readonly HashSet<string> _favorites = [];
    private Song _song = new() { Title = "未播放", Artist = "", Untimed = true };
    private int _songIndex = -1, _anchor = -2, _frameCount, _lastFpsFrames, _loadGeneration;
    private long _navigationRequest;
    private bool _navigationPlaying;
    private bool _resetWordRuns = true;
    private long _renderedGeneration = -1;
    private double _lastWall, _lastFps, _manualUntil, _scrollFrom, _scrollTo, _scrollStart, _scrollDuration, _noticeUntil;
    private bool _portrait, _fullscreen, _disposed, _rebuildPending = true, _autoScrolling, _shuffle, _repeat, _audioLoaded, _suppressMediaEnded, _verticalControlsShown, _breathing, _pageKaraoke;
    private Point _touchStart;
    private Point? _pointerAtScroll;
    private Rect _restoreBounds;
    private WindowState _restoreState;
    private SystemMediaConnection? _system;
    private bool _remoteShuffle, _remoteRepeat;
    private MediaPlaybackAutoRepeatMode? _remoteRepeatMode;
    private byte[]? _mediaArtwork;
    private bool _hasExplicitArtwork;
    private bool _updatingWindowChrome;
    private bool _independentPresentation;
    private readonly WindowChrome _lyricsChrome = new() { CaptionHeight = 0, ResizeBorderThickness = new Thickness(5), CornerRadius = new CornerRadius(0) };
    private FontFamily _font = new("Segoe UI");
    private readonly int _singOpacityDuration;
    public int RenderedFrameCount => _frameCount;
    public Song CurrentSong => _song;
    public PlaybackClock Clock => _clock;
    public ViewOptions Options => _options;
    public bool Portrait => _portrait;
    public string? BackgroundError => _background.LastError;

    public MainWindow(string[] args, bool hostedByPlayer = false)
    {
        _args = args;
        _hostedByPlayer = hostedByPlayer;
        _songs = args.Contains("--verify-offscreen") ? DemoSongs.Create() : [];
        _sleepPrevention = args.Contains("--verify-offscreen") ? new(_ => throw new InvalidOperationException("Offscreen verification must not call the system execution-state API.")) : new();
        if (args.Contains("--self-test") || args.Contains("--verify-offscreen"))
        {
            _options = new();
            _options.Background = BackgroundMode.Legacy;
            _options.ReduceMotion = true;
        }
        _singOpacityDuration = _options.AppleMusicLyrics.LineSwitchingAppleIosStyle164 ? 700 : 350;
        Title = "Cliric · Apple Music Lyrics";
        Width = 1240;
        Height = 820;
        MinWidth = 490;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = hostedByPlayer ? WindowStyle.SingleBorderWindow : WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        Background = Brushes.Black;
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        if (!hostedByPlayer)
            WindowChrome.SetWindowChrome(this, _lyricsChrome);
        Topmost = _options.Topmost;
        ReferenceMenus.UpdateTheme(_options.LightTheme);
        BuildResources();
        BuildPage();
        _background.Ready += () => UpdateIndependentPresentation(_background.IndependentRendererActive && _background.RendererReady);
        _background.Fault += message => { UpdateIndependentPresentation(false); App.Log("Background: " + message); Notify("背景渲染器切换到兼容模式"); };
        _audio.MediaOpened += (_, _) =>
        {
            _audioOpening = false;
            UpdatePlaybackAvailability();
            if (_audio.NaturalDuration.HasTimeSpan)
            {
                double p = _clock.PositionMs;
                bool play = _clock.IsPlaying;
                _song.DurationMs = _audio.NaturalDuration.TimeSpan.TotalMilliseconds;
                _clock.Load(_song.DurationMs);
                _clock.Seek(p);
                if (play)
                    _clock.Play();
            }
        };
        _audio.MediaEnded += (_, _) => { if (!_suppressMediaEnded) EndTrack(); };
        _audio.MediaFailed += (_, e) =>
        {
            if (!_audioOpening && !_audioLoaded)
                return;
            _audioOpening = _audioLoaded = false;
            _clock.Pause();
            UpdatePlayButtons();
            Notify("音频无法播放：" + e.ErrorException.Message);
            App.Log(e.ErrorException.ToString());
        };
        Loaded += OnLoaded;
        SizeChanged += (_, _) => _rebuildPending = true;
        StateChanged += (_, _) => { if (_updatingWindowChrome) return; UpdateWindowChrome(); _rebuildPending = true; UpdateLyricsVisibility(); };
        if (_hostedByPlayer)
            IsVisibleChanged += (_, _) => UpdateLyricsVisibility();
        Unloaded += (_, _) => ReleaseSleepRequest();
        PreviewKeyDown += OnKey;
        Closed += OnClosed;
        PreviewMouseUp += (_, e) => { if (_wall.Elapsed.TotalSeconds < _ignoreDialogMouseUntil) e.Handled = true; };
        AllowDrop = true;
        Drop += OnDrop;
        if (_hostedByPlayer)
            BuildShell();
    }

    private void BuildResources()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 14d));
        style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = border }));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(28, 255, 255, 255))));
        style.Triggers.Add(hover);
        _root.Resources.Add(typeof(Button), style);
    }
    private void UpdateWindowChrome()
    {
        if (_updatingWindowChrome)
            return;
        bool maximized = WindowState == WindowState.Maximized;
        if (_hostedByPlayer && !_fullscreen)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            if (WindowChrome.GetWindowChrome(this) != _libraryChrome)
                WindowChrome.SetWindowChrome(this, _libraryChrome);
            if (!maximized)
                _shell.Margin = default;
            double headerBottom = _library!.TitleBar.Height + _shell.Margin.Top;
            _libraryChrome.CaptionHeight = _lyricsShown ? 0 : Math.Max(0, headerBottom - _libraryChrome.ResizeBorderThickness.Top);
            _library!.UpdateMaximizeCaption(maximized);
            Topmost = !maximized && _options.Topmost;
            UpdateIndependentPresentation(_background.IndependentRendererActive && _background.RendererReady);
            UpdateGlassFrame();
            return;
        }

        WindowStyle = WindowStyle.None;
        if (WindowChrome.GetWindowChrome(this) != _lyricsChrome)
            WindowChrome.SetWindowChrome(this, _lyricsChrome);
        _lyricsChrome.ResizeBorderThickness = new Thickness(maximized ? 0 : 5);
        bool fixedSize = (maximized || _fullscreen) && BackgroundView.SupportsIndependent(_options.Background) && _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer;
        if (maximized && fixedSize && ResizeMode != ResizeMode.NoResize)
        {
            _updatingWindowChrome = true;
            try
            {
                WindowState = WindowState.Normal;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            finally { _updatingWindowChrome = false; }
        }
        else
            ResizeMode = fixedSize ? ResizeMode.NoResize : ResizeMode.CanResize;
        if (WindowState is WindowState.Normal or WindowState.Maximized)
        {
            ((FrameworkElement)Content).Margin = new Thickness(maximized && !fixedSize ? 6 : 0);
            Topmost = !maximized && _options.Topmost;
        }
        UpdateIndependentPresentation(_background.IndependentRendererActive && _background.RendererReady);
        UpdateGlassFrame();
    }

    private void UpdateGlassFrame()
    {
        if (_hostedByPlayer && !_fullscreen)
        {
            _libraryChrome.GlassFrameThickness = _independentPresentation ? new Thickness(-1) :
                new Thickness(0, _library!.TitleBar.Height + _shell.Margin.Top, 0, 0);
        }
        else if (_independentPresentation)
            _lyricsChrome.GlassFrameThickness = new Thickness(-1);
        else
            _lyricsChrome.ClearValue(WindowChrome.GlassFrameThicknessProperty);
    }

    private void UpdateIndependentPresentation(bool ready)
    {
        if (_disposed)
            return;
        bool transparent = ready && CanPresentIndependentBackground && WindowState == WindowState.Maximized && BackgroundView.SupportsIndependent(_options.Background) && _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer && !_options.ReduceMotion;
        if (_independentPresentation == transparent)
            return;
        _independentPresentation = transparent;
        Background = _background.Background = transparent ? Brushes.Transparent : Brushes.Black;
        if (_hostedByPlayer)
            _shell.Background = Background;
        UpdateGlassFrame();
        if (PresentationSource.FromVisual(this) is HwndSource source && source.CompositionTarget is { } target)
            target.BackgroundColor = transparent ? Colors.Transparent : Colors.Black;
    }
    private Button Button(string label, string automation, Action action, double width = 36, double height = 36)
    {
        var b = new Button { Content = label, Width = width, Height = height, ToolTip = automation, Margin = new Thickness(2) };
        AutomationProperties.SetName(b, automation);
        b.Click += (_, _) => Guard(action);
        return b;
    }
    private void BuildPage()
    {
        Content = _root;
        _root.Children.Add(_background);
        var family = ReferencePageView.FontFor(FontPresets.SpotifyMix);
        _reference = new ReferencePageView(family, _args.Contains("--verify-offscreen"));
        _root.Children.Add(_reference.View);
        _blend = new PlusLighterComposer(_reference);
        _reference.View.DataContext = this;
        _stage = _reference.Get<Grid>("MainGrid");
        _leftColumn = _reference.Get<ColumnDefinition>("ColumnDefinitionLeftSide");
        _lyricHost = _reference.Get<Grid>("GridLyrics");
        _scroll = _reference.Get<TouchScrollViewer>("ScrollViewerLyrics");
        _scroll.WheelMotionReduced = () => _options.ReduceMotion;
        _linesPanel = _reference.Get<Panel>("StackPanelLyrics");
        _infoView = _reference.Get<Viewbox>("ViewboxSongInfo");
        _smallInfo = _reference.Get<Viewbox>("ViewboxSongInfoSmall");
        _verticalControls = _reference.Get<Grid>("GridVerticalControls");
        _artwork = _reference.Get<ArtworkImage>("WebImageAlbum");
        _smallArtwork = _reference.Get<ArtworkImage>("WebImageAlbumSmall");
        InitializeArtworkVideo();
        _title = _reference.Get<TextBlock>("TextBlockTrackName");
        _artist = _reference.Get<TextBlock>("TextBlockArtist");
        _smallTitle = _reference.Get<TextBlock>("TextBlockVerticalModeTrackName");
        _smallArtist = _reference.Get<TextBlock>("TextBlockVerticalModeArtistName");
        _seek = _reference.Get<ReferenceStateBar>("AppleMusicPlayerStateBar");
        _seekVertical = _reference.Get<ReferenceStateBar>("AppleMusicPlayerStateBarVertical");
        _dots = _reference.Get<BreathingDots>("AppleMusicBreathingDots");
        _magic = _reference.Get<Border>("BorderMagicButtonOutside");
        if (!_args.Contains("--verify-offscreen"))
            _dots.ConfigurePlayback(() => _clock.PositionMs + _options.TimelineOffsetMs, () => _clock.IsPlaying, () => _wall.Elapsed.TotalMilliseconds);
        _fps = _reference.Get<TextBlock>("TextBlockFps");
        _seek.SeekRequested += Seek;
        _seekVertical.SeekRequested += Seek;
        foreach (var button in _reference.Elements.OfType<ImageButton>())
        {
            string handler = InputMap.GetClick(button);
            button.Click += (_, _) => Guard(() =>
            {
                switch (handler)
                {
                    case "ImageClickablePauseButton_Click":
                    case "ImageClickablePlayButton_Click":
                        TogglePlay();
                        break;
                    case "ImageClickablePrevious_Click":
                        Previous();
                        break;
                    case "ImageClickableNext_Click":
                        Next();
                        break;
                    case "ImageClickableShuffle_Click":
                        ToggleShuffle();
                        break;
                    case "ImageClickableRepeat_Click":
                        ToggleRepeat();
                        break;
                }
            });
        }
        foreach (var favorite in _reference.Elements.OfType<FavoriteButton>())
            favorite.Click += (_, _) => Favorite();
        foreach (var name in new[] { "GridThreeDotMenu", "GridThreeDotMenuVerticalMode" })
            _reference.Get<Grid>(name).MouseLeftButtonUp += (sender, _) => { RefreshTrackMenu(); var menu = _reference.Get<ContextMenu>("MenuThreeDot"); menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true; };
        foreach (var (hitName, targetName) in new[] { ("BorderFavoriteButtonMainForMousePoint", "FavoriteButtonMain"), ("BorderFavoriteButtonVerticalModeForMousePoint", "FavoriteButtonVerticalMode"), ("BorderMenuBackgroundForMousePoint", "GridThreeDotMenu"), ("BorderMenuVerticalModeBackgroundForMousePoint", "GridThreeDotMenuVerticalMode") })
        {
            var hit = _reference.Get<Border>(hitName);
            var target = _reference.Get<FrameworkElement>(targetName);
            var menu = _reference.Get<ContextMenu>("MenuThreeDot");
            _buttonDrags.Add(new(hit, target, () => _options.AppleMusicLyrics.EnableAdvancedButtonDragAnimation, () => !_options.ReduceMotion, () =>
            {
                if (target is FavoriteButton favorite)
                    favorite.Activate();
                else
                {
                    RefreshTrackMenu();
                    menu.PlacementTarget = hit;
                    menu.IsOpen = true;
                }
            }, target is FavoriteButton heart ? heart.SetPressedVisual : null, () => menu.IsOpen));
        }
        _reference.Get<ContextMenu>("MenuThreeDot").Closed += (_, _) => { foreach (var drag in _buttonDrags) drag.Reset(); };
        _reference.Get<Grid>("GridArtwork").RenderTransform = _artworkScale;
        _artwork.MouseLeftButtonUp += (_, _) => OpenCover();
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                ChangeZoom(e.Delta > 0);
                e.Handled = true;
                return;
            }
            SetVerticalControls(e.Delta > 0);
            ManualScroll();
        };
        _scroll.PreviewTouchDown += (_, e) => { _scroll.StopWheelAnimation(); _touchStart = e.GetTouchPoint(_scroll).Position; ManualScroll(); };
        _scroll.PreviewTouchMove += (_, e) => { ManualScroll(); double delta = e.GetTouchPoint(_scroll).Position.Y - _touchStart.Y; if (delta > 150) SetVerticalControls(true); else if (delta < -150) SetVerticalControls(false); };
        _lyricHost.SizeChanged += (_, _) => _rebuildPending = true;
        _magicController = new MagicStripController(_reference, () => WindowState == WindowState.Maximized, () => _options.ReduceMotion, ExecuteMagic, () => _options.AppleMusicLyrics, () => Guard(() => App.SaveOptions(_options)));
        _reference.Get<Grid>("GridMagicButton").MouseLeftButtonDown += (_, e) => { if (!e.Handled && e.ClickCount == 1 && e.LeftButton == MouseButtonState.Pressed) Guard(DragMove); };
        _status.FontSize = 10;
        _status.Foreground = Brushes.White;
        _status.Opacity = .45;
        _status.Margin = new Thickness(18, 0, 18, 12);
        _status.VerticalAlignment = VerticalAlignment.Bottom;
        _status.IsHitTestVisible = false;
        _status.Visibility = Visibility.Collapsed;
        _root.Children.Add(_status);
        ConfigureOverlay(_queue, 290);
        var queueDock = new DockPanel();
        _queue.Child = queueDock;
        var queueHeader = new TextBlock { Text = "播放列表", Foreground = Brushes.White, FontSize = 18, Margin = new Thickness(12) };
        DockPanel.SetDock(queueHeader, Dock.Top);
        queueDock.Children.Add(queueHeader);
        queueDock.Children.Add(new ScrollViewer { Content = _queueItems, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _root.Children.Add(_queue);
        ContextMenu = BuildContextMenu();
        ConnectTrackMenus();
    }
    private static void ConfigureOverlay(Border border, double width)
    {
        border.Width = width;
        border.HorizontalAlignment = HorizontalAlignment.Right;
        border.VerticalAlignment = VerticalAlignment.Stretch;
        border.Margin = new Thickness(15, 45, 15, 20);
        border.Padding = new Thickness(14);
        border.CornerRadius = new CornerRadius(16);
        border.Background = new SolidColorBrush(Color.FromArgb(244, 28, 29, 40));
        border.Visibility = Visibility.Collapsed;
        border.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        border.BorderThickness = new Thickness(1);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateSleepRequest();
        await StartPlaybackAsync();
        if (_disposed || _lyricsLoaded || _hostedByPlayer)
            return;
        _lyricsLoaded = true;
        _magicController.BeginTutorial(_wall.Elapsed.TotalMilliseconds);
    }
    public Task LoadSongAsync(int index)
    {
        _navigationRequest = 0;
        _navigationPlaying = false;
        _pendingQueueEntry = null;
        return LoadSongAsync(index, ++_queueSelection, _queueLifetime.Token);
    }

    private Task LoadSongAsync(int index, long selection, CancellationToken token) =>
        WhilePlaybackLoadsAsync(() => LoadSongCoreAsync(index, selection, token));

    private async Task LoadSongCoreAsync(int index, long selection, CancellationToken token)
    {
        if (_disposed || _songs.Count == 0)
            return;
        var requested = _songs[(index % _songs.Count + _songs.Count) % _songs.Count];
        int generation = _loadGeneration;
        using var load = CancellationTokenSource.CreateLinkedTokenSource(token, _queueLifetime.Token);
        await PrepareQueuedLyricsAsync(requested, load.Token);
        if (_disposed || selection != _queueSelection || generation != _loadGeneration || (index = _songs.IndexOf(requested)) < 0)
            return;
        load.Token.ThrowIfCancellationRequested();
        _dots.StopPlayback();
        StopArtworkVideo();
        generation = ++_loadGeneration;
        _seek.CancelPreview();
        _seekVertical.CancelPreview();
        _system?.Dispose();
        _system = null;
        _mediaArtwork = null;
        _suppressMediaEnded = true;
        _audio.Stop();
        _audio.Close();
        _audioOpening = _audioLoaded = false;
        _suppressMediaEnded = false;
        _songIndex = index;
        _song = requested;
        _playNext.Remove(requested);
        _scroll.ScrollToVerticalOffset(0);
        _autoScrolling = false;
        IsHearted = _favorites.Contains(_song.Id);
        foreach (var favorite in _reference.Elements.OfType<FavoriteButton>())
            favorite.ResetVisual();
        _clock.Load(_song.DurationMs);
        _anchor = -2;
        _manualUntil = 0;
        _resetWordRuns = true;
        _title.Text = _smallTitle.Text = _song.Title;
        _artist.Text = _smallArtist.Text = _song.Artist;
        if (_netEaseActive)
            await StopNetEaseAsync();
        if (generation != _loadGeneration || _disposed)
            return;
        BitmapSource cover;
        _hasExplicitArtwork = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(_song.CoverPath) && File.Exists(_song.CoverPath))
            {
                cover = Artwork.Load(_song.CoverPath);
                _hasExplicitArtwork = true;
            }
            else
                cover = Artwork.Create(_songIndex);
        }
        catch (Exception ex) { App.Log(ex.ToString()); cover = Artwork.Create(_songIndex); }
        _artwork.Source = _smallArtwork.Source = cover;
        await _background.SetModeAsync(_options.Background, _options);
        if (generation != _loadGeneration || _disposed)
            return;
        await _background.SetArtworkAsync(cover, _song.Id);
        if (generation != _loadGeneration || _disposed)
            return;
        if (!string.IsNullOrWhiteSpace(_song.AudioPath) && File.Exists(_song.AudioPath))
            await OpenAudioFileAsync(_song.AudioPath);
        _rebuildPending = true;
        RefreshQueue();
        UpdatePlayButtons();
        if (!_hasExplicitArtwork && _netEaseTracks.TryGetValue(_song.Id, out var track) && track.CoverUrl.Length > 0)
            _ = UpdateNetEaseCoverAsync(track.CoverUrl, generation);
    }
    private TimeSpan? _lastCompositorFrame;

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs frame)
        {
            if (_lastCompositorFrame == frame.RenderingTime)
                return;
            _lastCompositorFrame = frame.RenderingTime;
        }
        if (_disposed)
            return;
        double now = _wall.Elapsed.TotalSeconds, dt = Math.Clamp(now - _lastWall, 0, .1);
        _lastWall = now;
        if (_hostedByPlayer)
        {
            UpdateBackground(dt);
            if (!LyricsVisible || _lyricsEntering)
                return;
        }
        _frameCount++;
        _magicController.AdvanceTutorial(now * 1000);
        _status.Visibility = now < _noticeUntil ? Visibility.Visible : Visibility.Collapsed;
        if (_rebuildPending && _reference.View.ActualWidth > 50)
        {
            _rebuildPending = false;
            RebuildRows();
        }
        double position = UpdatePlaybackPosition();
        _seek.IsRealTimeUpdate = _seekVertical.IsRealTimeUpdate = _options.RealtimeSeek;
        _seek.ReduceMotion = _seekVertical.ReduceMotion = _options.ReduceMotion;
        _seek.AnimatedTime = _seekVertical.AnimatedTime = _options.AnimatedTime;
        _seek.SetPosition(position, _clock.DurationMs);
        _seekVertical.SetPosition(position, _clock.DurationMs);
        double lyricTime = position + _options.TimelineOffsetMs;
        double wordTime = lyricTime - LyricsPreparation.TimelineCompensationMs;
        double? animationTime = _args.Contains("--verify-offscreen") ? null : now * 1000;
        int anchor = Timeline.Anchor(_groups, lyricTime);
        bool discontinuity = _renderedGeneration != _clock.Generation;
        if (discontinuity)
        foreach (var row in _rows)
        {
            row.Pull.BeginAnimation(TranslateTransform.YProperty, null);
            row.Pull.Y = 0;
        }
        bool slotChange = _rows.Any(r => r.Backgrounds.Any(b => b.SlotOpen != Timeline.Active(r.Data, lyricTime)));
        CapturePullLayout();
        ConfigureSwitchDelays(anchor, lyricTime, now);
        double[]? previous = slotChange && !discontinuity && !_options.ReduceMotion ? _rows.Select(r => VisualTreeHelper.GetOffset(r.Host).Y + r.Pull.Y).ToArray() : null;
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            bool active = Timeline.Active(row.Data, lyricTime);
            double start = row.Data.Untimed ? double.PositiveInfinity : row.Data.StartMs;
            foreach (var backing in row.Backgrounds)
                backing.Update(wordTime, lyricTime, active, i - anchor, start, row.Data.EndMs, _clock.Generation, animationTime);
            row.Main.Update(wordTime, lyricTime, active, i - anchor, start, row.Data.EndMs, _clock.Generation, animationTime,
                completeNaturalTail: true);
        }
        if (_credit is not null)
            ((StackPanel)_credit.FindName("StackPanelLyrics")).Opacity = anchor >= _rows.Count ? 1 : _options.Blur ? .4 : .3;
        if (slotChange)
        {
            _linesPanel.UpdateLayout();
            if (previous is not null && (!_options.AppleMusicLyrics.PullingEffect || now < _manualUntil))
                for (int i = 0; i < _rows.Count; i++)
                {
                    var row = _rows[i];
                    double displacement = previous[i] - VisualTreeHelper.GetOffset(row.Host).Y - row.Pull.Y;
                    if (Math.Abs(displacement) < .1)
                        continue;
                    AnimatePull(i, displacement, 0, Motion.ScrollDurationMs(_options.AppleMusicLyrics, _pageKaraoke) * _options.AnimationSpeed, (int)Motion.PullingDelayMs(Math.Max(0, i - anchor), _options.AnimationSpeed, _options.AppleMusicLyrics.PullingDelay, _options.AppleMusicLyrics.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS, row.Main.OpacityTransition.UseBackEase), row.Main.OpacityTransition);
                }
        }
        _renderedGeneration = _clock.Generation;
        var gap = Timeline.BreathingGap(_groups, position + _options.TimelineOffsetMs);
        if (gap is { } breathing && !_song.Untimed)
        {
            var nextLine = _groups.FirstOrDefault(x => x.StartMs >= breathing.End);
            _dots.SetAlignment(_options.Duet ? (nextLine?.Main.AlignLeft == false ? HorizontalAlignment.Right : HorizontalAlignment.Left) : null, _options.AppleMusicLyrics);
            if (animationTime is null)
                _dots.Advance(position + _options.TimelineOffsetMs, breathing.Start, breathing.End, breathing.Start > 0, _options.ReduceMotion);
        }
        if (animationTime is { } dotsClock)
            _dots.AdvancePlayback(position, _song.Untimed ? null : gap, dotsClock, _clock.IsPlaying, _clock.Generation, _song.Id, _options, now >= _manualUntil, _pageKaraoke);
        else if (gap is null || _song.Untimed)
            _dots.Visibility = Visibility.Collapsed;
        bool breathingNow = gap is not null && !_song.Untimed && (animationTime is null || _dots.PlaybackActive);
        bool breathingChanged = _breathing != breathingNow;
        _breathing = breathingNow;
        bool resumeOnVisibleLine = _manualUntil > 0 && anchor != _anchor && !breathingNow && IsLyricLineInView(anchor);
        bool scrollInputActive = _scroll.IsWheelAnimating || _scroll.AreAnyTouchesOver || _seekVertical.Dragging;
        if (!scrollInputActive && (now >= _manualUntil || resumeOnVisibleLine) && (anchor != _anchor || breathingChanged || slotChange || _manualUntil > 0))
        {
            _anchor = anchor;
            _manualUntil = 0;
            Follow(anchor, breathingNow, allowPulling: !resumeOnVisibleLine || now - _lastManualScroll > .5);
        }
        if (_autoScrolling)
        {
            double p = (now - _scrollStart) / _scrollDuration;
            if (p >= 1)
            {
                _scroll.SetFrameOffset(_scrollTo);
                _autoScrolling = false;
            }
            else
                _scroll.SetFrameOffset(_scrollFrom + (_scrollTo - _scrollFrom) * ScrollEase(p, _scrollTransition));
        }
        if (!_hostedByPlayer)
            UpdateBackground(dt);
        UpdateArtworkScale();
        UpdateArtworkVideo();
        _reference.Get<Border>("BorderFpsOverlay").Visibility = _options.ShowFps ? Visibility.Visible : Visibility.Collapsed;
        if (now - _lastFps >= _options.AppleMusicLyrics.FpsOverlayUpdateInterval)
        {
            _fps.Text = $"{(_frameCount - _lastFpsFrames) / Math.Max(now - _lastFps, .001):0} FPS";
            _lastFps = now;
            _lastFpsFrames = _frameCount;
        }
    }

    private bool UseReferenceBackdrop =>
        _options.Background is BackgroundMode.Static or BackgroundMode.Legacy or BackgroundMode.DynamicBeating || _options.ReduceMotion;

    private void UpdateBackground(double elapsed)
    {
        bool visible = IsVisible && WindowState != WindowState.Minimized;
        bool portrait = _hostedByPlayer ? IsPortrait(_shell.ActualWidth, _shell.ActualHeight) : _portrait;
        bool legacy = UseReferenceBackdrop;
        _reference.SetLegacyVisible(legacy);
        if (legacy)
        {
            if (_shellLegacyBackdrop is { } shared)
                shared.Margin = portrait ? new Thickness(-6, 0, -6, 0) : default;
            if (!_options.ReduceMotion && visible)
                _legacyTime += elapsed;
            int rate = Math.Clamp(_options.AppleMusicLyrics.BackgroundDynamicFrameRate, 1, 120);
            _reference.UpdateLegacy(Math.Floor(_legacyTime * rate) / rate, _options.LightTheme, _options.Background == BackgroundMode.Static || _options.ReduceMotion);
        }
        _background.Update(_clock.IsPlaying, portrait, visible, _options, elapsed,
            holdInlineFrame: _hostedByPlayer && LyricsProgress is > 0 and < 1);
        UpdateIndependentPresentation(_background.IndependentRendererActive && _background.RendererReady);
        _background.Visibility = legacy ? Visibility.Hidden : Visibility.Visible;
    }

    private static bool IsPortrait(double width, double height) => height / Math.Max(1, width) > .85;

    private void RebuildRows()
    {
        if (!_args.Contains("--verify-offscreen"))
            _dots.RestartForLayout();
        double pageWidth = _reference.View.ActualWidth, pageHeight = _reference.View.ActualHeight;
        _portrait = IsPortrait(pageWidth, pageHeight);
        bool compact = _portrait;
        bool empty = _song.Lines.Count == 0;
        _leftColumn.Width = compact && !empty ? new GridLength(0) : new GridLength(.82, GridUnitType.Star);
        _reference.Get<ColumnDefinition>("ColumnDefinitionRightSide").Width = empty ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        _lyricHost.Visibility = empty ? Visibility.Hidden : Visibility.Visible;
        _infoView.Visibility = compact && !empty ? Visibility.Collapsed : Visibility.Visible;
        _smallInfo.Visibility = compact && !empty ? Visibility.Visible : Visibility.Hidden;
        _stage.Margin = _portrait ? new Thickness(-6, 0, -6, 0) : new Thickness(0);
        _infoView.Margin = new Thickness(0, _portrait ? 50 : 75, 0, _portrait ? 50 : 75);
        _reference.Get<StackPanel>("StackPanelSongInfo").Margin = _portrait ? new Thickness(-20, 0, -20, 0) : new Thickness(0);
        _verticalControls.MaxHeight = pageHeight / 3;
        if (!compact || empty)
            _verticalControlsShown = false;
        _verticalControls.Visibility = compact && !empty && _verticalControlsShown ? Visibility.Visible : Visibility.Hidden;
        Grid.SetColumnSpan(_reference.Get<Grid>("GridMagicButton"), compact ? 2 : 1);
        _reference.Get<Grid>("GridMagicButton").Height = compact ? 90 : 120;
        _magic.Visibility = _options.MagicStrip ? Visibility.Visible : Visibility.Collapsed;
        foreach (var drag in _buttonDrags)
            drag.Refresh();
        _reference.SetMotion(!_options.ReduceMotion, _options.AppleMusicLyrics.EnableTrackInfoMarquee);
        _font = ReferencePageView.FontFor(_options.AppleMusicLyrics.FontPreset, _options.AppleMusicLyrics.FontFamily);
        _reference.View.FontFamily = _font;
        _seek.FontFamily = _options.AppleMusicLyrics.FontPreset == FontPresets.SFPro ? ReferencePageView.AppleTextFont() : _font;
        _seekVertical.FontFamily = _font;
        _seek.FontWeight = _options.Bold ? FontWeights.Bold : FontWeights.Medium;
        _reference.ApplyMobileInfoStyle(_options.AppleMusicLyrics.IosStylePreset);
        foreach (var menu in _reference.Elements.OfType<ContextMenu>())
            menu.FontFamily = _font;
        UpdateZoomContext(compact, _fullscreen || WindowState == WindowState.Maximized, pageHeight);
        double scale = 1 / _inverseZoom;
        _displayScale = scale;
        var lyricsView = _reference.Get<Grid>("ViewBoxLyrics");
        lyricsView.Margin = compact ? new Thickness(0, -95 + 185 * scale, 0, 0) : new Thickness(0, 0, 30, 0);
        double width = Math.Max(100, _lyricHost.ActualWidth - lyricsView.Margin.Right);
        _scroll.Width = width;
        _scroll.Height = pageHeight;
        var scrollGrid = _reference.Get<Grid>("GridLyricsInViewbox");
        scrollGrid.Width = width;
        scrollGrid.Height = pageHeight;
        _reference.Get<Grid>("GridSongInfoSmall").Width = width / scale;
        _reference.Get<Grid>("GridSongInfoSmallOverlay").Width = width / scale;
        var verticalBox = _reference.Get<Viewbox>("ViewboxVerticalControls");
        verticalBox.Margin = new Thickness(20 * scale * 1.37, verticalBox.Margin.Top, 20 * scale * 1.37, verticalBox.Margin.Bottom);
        _linesPanel.Margin = new Thickness(8.2 * scale, 450 * scale, 8.2 * scale, 0);
        ((SpacedStackPanel)_linesPanel).Spacing = 4 * scale;
        double infoHeight = _reference.Get<StackPanel>("StackPanelSongInfo").ActualHeight;
        double albumCenter = (_stage.ActualHeight - _infoView.ActualHeight) / 2 + _infoView.ActualHeight * 165 / Math.Max(1, infoHeight);
        _dots.Width = 60 * scale;
        _dots.Margin = new Thickness(24 * scale, compact ? 130 - 10 * scale : albumCenter - 25 * scale, 24 * scale, 0);
        var previousRows = _resetWordRuns ? [] : _rows.ToArray();
        _resetWordRuns = false;
        _linesPanel.Children.Clear();
        _rows.Clear();
        _credit = null;
        var groups = LyricsPreparation.ForPage(_song, _options.Duet, _options.RightAlignFirst, _options.AppleMusicLyrics, _options.EnableMultiLine);
        _groups = groups;
        bool karaoke = _pageKaraoke = LyricsPreparation.UseKaraoke(_song, _options.AppleMusicLyrics);
        int rowIndex = 0;
        foreach (var data in groups)
        {
            var host = new StackPanel();
            bool duetInset = _options.Duet && _song.Lines.Any(x => x.AlignLeft == false);
            bool useFractionalLift = WindowState == WindowState.Normal && !_fullscreen;
            var main = new LyricRow(data.Main, data.Right, false, width - 16.4 * scale, scale, _font, _options, duetInset,
                portrait: _portrait, karaoke: karaoke, haveRomanization: _song.Lines.Any(l => l.Romanization.Length > 0))
            { UseFractionalLift = useFractionalLift };
            main.SeekRequested += value => Seek(value);
            main.HoverAllowed = () => _pointerAtScroll is not { } point || Mouse.GetPosition(_reference.View) != point;
            main.AnimationClockNow = () => _wall.Elapsed.TotalMilliseconds;
            main.OpacityTransition = LineMotion.PageTransition(groups, rowIndex, karaoke, _options.AppleMusicLyrics);
            main.NextLineStartMs = karaoke ? WordSweep.NextLineStart(groups, rowIndex) : -1;
            RowSlot? previousRow = rowIndex < previousRows.Length ? previousRows[rowIndex] : null;
            rowIndex++;
            bool overlap = _options.AppleMusicLyrics.BuggyApple && _options.AppleMusicLyrics.BuggyAppleOptions.BackgroundVocalsOverlap && (rowIndex == 1 ? data.StartMs > 6500 : data.StartMs - groups[rowIndex - 2].EndMs > 6000);
            if (previousRow is not null)
                main.RestoreWordRun(previousRow.Main);
            main.SingOpacityDuration = _singOpacityDuration;
            main.HaveAnyTranslation = _song.Lines.Any(l => !string.IsNullOrWhiteSpace(l.Translation));
            main.RefreshCopyMenu();
            main.CopyRequested += (line, kind) => Guard(() => { Clipboard.SetText(CopyText.Build(kind, line, _song.Lines, _groups.Select(g => g.Main))); Notify("已复制"); });
            var backingRows = new List<LyricRow>();
            host.Children.Add(main);
            foreach (var backing in data.Backgrounds.Where(b => b.StartMs < data.Main.StartMs))
                AddBacking(backing);
            foreach (var backing in data.Backgrounds.Where(b => b.StartMs >= data.Main.StartMs))
                AddBacking(backing);
            if (previousRow is not null)
            for (int i = 0; i < Math.Min(backingRows.Count, previousRow.Backgrounds.Count); i++)
                backingRows[i].RestoreWordRun(previousRow.Backgrounds[i]);
            _linesPanel.Children.Add(host);
            var slot = new RowSlot(data, host, main, backingRows);
            host.RenderTransform = slot.Pull;
            _rows.Add(slot);
            void AddBacking(LyricLine line)
            {
                bool above = _options.AppleMusicLyrics.BackgroundVocalsNativePosition && line.StartMs < data.Main.StartMs;
                var row = new LyricRow(line, data.Right, true, main.TextWidth, scale, _font, _options, duetInset, above, _portrait, karaoke,
                    haveRomanization: _song.Lines.Any(l => l.Romanization.Length > 0))
                {
                    UseFractionalLift = useFractionalLift,
                    BackgroundOverlap = overlap,
                    OpacityTransition = main.OpacityTransition,
                    BackgroundExitTransition = rowIndex < groups.Count ? LineMotion.PageTransition(groups, rowIndex, karaoke, _options.AppleMusicLyrics) : (false, .2),
                    SingOpacityDuration = _singOpacityDuration
                };
                main.AddBackground(row, above);
                backingRows.Add(row);
            }
        }
        if (groups.Count == 0)
            _linesPanel.Children.Add(new TextBlock { Text = "暂无歌词\n导入 LRC、TTML 或 JSON 歌词", FontSize = 25, Foreground = Brushes.White, Opacity = .5, Margin = new Thickness(30) });
        if (!string.IsNullOrWhiteSpace(_song.Credit))
        {
            _credit = (UserControl)Application.LoadComponent(new Uri("/Cliric;component/Assets/ReferenceCredit.xaml", UriKind.Relative));
            _credit.FontFamily = _font;
            _credit.FontSize = (compact ? 28 : 24) * scale;
            ((StackPanel)_credit.FindName("StackPanelLyrics")).Margin = new Thickness(20 * scale, 15 * scale, 20 * scale, 15 * scale);
            ((System.Windows.Documents.Run)_credit.FindName("RunWrittenBy")).Text = "";
            var writers = (System.Windows.Documents.Run)_credit.FindName("RunWriters");
            writers.Text = _song.Credit;
            writers.FontWeight = _options.Bold ? FontWeights.Bold : FontWeights.Medium;
            ((TextBlock)_credit.FindName("TextBlockLyrics")).LineHeight = 30 * scale;
            _ = new RowFeedback(_credit, (Grid)_credit.FindName("GridInside"), (Border)_credit.FindName("BorderBackground"), () => !_options.ReduceMotion, () => _pointerAtScroll is not { } point || Mouse.GetPosition(_reference.View) != point);
            _credit.MouseLeftButtonUp += (_, _) => Seek(Math.Max(0, _song.DurationMs - 1000));
            _linesPanel.Children.Add(_credit);
        }
        _linesPanel.Children.Add(new Border { Height = Math.Max(250, _scroll.ActualHeight * .65) });
        // The compact information view must have its new height before its fade is measured.
        _root.UpdateLayout();
        _scroll.OpacityMask = null;
        var maskHost = _reference.Get<Grid>("GridLyricsMaskHost");
        if (compact)
        {
            double h = _smallInfo.ActualHeight, gridHeight = _reference.Get<Grid>("GridSongInfoSmall").ActualHeight;
            double top = 15 + h / (gridHeight + 70) * (gridHeight + 35);
            var mask = new LinearGradientBrush { StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1) };
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, top / Math.Max(1, pageHeight)));
            mask.GradientStops.Add(new GradientStop(Colors.White, (top + 65 * scale) / Math.Max(1, pageHeight)));
            maskHost.OpacityMask = mask;
            mask.GradientStops.Add(new GradientStop(Colors.White, _verticalControlsShown ? 1 - (_verticalControls.ActualHeight + 100) / Math.Max(1, pageHeight) : 1));
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, _verticalControlsShown ? 1 - (_verticalControls.ActualHeight - 5) / Math.Max(1, pageHeight) : 1));
        }
        else
            maskHost.OpacityMask = null;
        bool sourceBackground = UseReferenceBackdrop;
        bool independent = BackgroundView.SupportsIndependent(_options.Background) && _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer && WindowState == WindowState.Maximized;
        _blend.Update(_options.AppleMusicLyrics.EnablePlusLighter && !independent, _options.AppleMusicLyrics.PlusLighterEffectPreset, sourceBackground ? _reference.Get<Viewbox>("ViewboxBackground") : _background, !sourceBackground);
        // Follow reads the new row offsets in this same rendering callback.
        _scroll.UpdateLayout();
        _anchor = -2;
        _autoScrolling = false;
    }
    private void SetVerticalControls(bool shown)
    {
        _verticalControlsShown = shown && _portrait;
        _verticalControls.Visibility = _verticalControlsShown ? Visibility.Visible : Visibility.Hidden;
        if (_reference.Get<Grid>("GridLyricsMaskHost").OpacityMask is LinearGradientBrush mask && mask.GradientStops.Count == 4)
        {
            double height = Math.Max(1, _lyricHost.ActualHeight);
            mask.GradientStops[2].Offset = _verticalControlsShown ? 1 - (_verticalControls.ActualHeight + 100) / height : 1;
            mask.GradientStops[3].Offset = _verticalControlsShown ? 1 - (_verticalControls.ActualHeight - 5) / height : 1;
        }
    }
    private void Follow(int index, bool breathing = false, bool allowPulling = true)
    {
        if (_song.Untimed)
            return;
        _pointerAtScroll = Mouse.GetPosition(_reference.View);
        _scrollFrom = _scroll.VerticalOffset;
        if (index < 0 && (!breathing || _rows.Count == 0))
            _scrollTo = 0;
        else if (index >= _rows.Count)
        {
            if (_credit is null)
            {
                _autoScrolling = false;
                return;
            }
            double y = VisualTreeHelper.GetOffset(_credit).Y;
            if (_portrait)
                _scrollTo = Math.Max(0, y + 475 * _displayScale - 130);
            else
            {
                double infoHeight = _reference.Get<StackPanel>("StackPanelSongInfo").ActualHeight;
                double albumCenter = (_stage.ActualHeight - _infoView.ActualHeight) / 2 + _infoView.ActualHeight * 165 / Math.Max(1, infoHeight);
                _scrollTo = Math.Max(0, y + 450 * _displayScale - albumCenter + (((StackPanel)_credit.FindName("StackPanelLyrics")).ActualHeight + 30 * _displayScale) / 2);
            }
        }
        else
        {
            int target = Math.Max(0, index);
            double occupied = 0;
            if (breathing && index >= 0 && index + 1 < _rows.Count)
            {
                target = index + 1;
                occupied = BackgroundSpace(_rows[index]);
                if (index > 0 && _rows[index - 1].Data.EndMs == _rows[index].Data.EndMs)
                    occupied += BackgroundSpace(_rows[index - 1]);
            }
            var row = _rows[target];
            double y = VisualTreeHelper.GetOffset(row.Host).Y - occupied;
            if (!breathing && index > 0 && (_groups[index].StartMs - _groups[index - 1].EndMs <= 6000))
            {
                y -= BackgroundSpace(_rows[index - 1]);
                if (index > 1 && _groups[index - 2].EndMs == _groups[index - 1].EndMs)
                    y -= BackgroundSpace(_rows[index - 2]);
            }
            if (_portrait)
                _scrollTo = Math.Max(0, y + 475 * _displayScale - 130 - (breathing ? 83 * _displayScale : 0));
            else
            {
                double infoHeight = _reference.Get<StackPanel>("StackPanelSongInfo").ActualHeight;
                double albumCenter = (_stage.ActualHeight - _infoView.ActualHeight) / 2 + _infoView.ActualHeight * 165 / Math.Max(1, infoHeight);
                _scrollTo = Math.Max(0, y + 450 * _displayScale - albumCenter + (breathing ? -43.5 * _displayScale : row.Host.ActualHeight / 2));
            }
        }
        var transition = index >= 0 && index < _rows.Count ? _rows[index].Main.OpacityTransition : (false, .2);
        if (index < 0 && breathing)
        {
            _autoScrolling = false;
            _scroll.ScrollToVerticalOffset(_scrollTo);
            return;
        }
        if (allowPulling && !_options.ReduceMotion && index >= 0 && index < _rows.Count && (breathing || _options.AppleMusicLyrics.PullingEffect))
        {
            ApplyPulling(_scrollTo, index, breathing, transition);
            return;
        }
        _scrollStart = _wall.Elapsed.TotalSeconds;
        _scrollDuration = _options.ReduceMotion ? .001 : Motion.ScrollDurationMs(_options.AppleMusicLyrics, _pageKaraoke) / 1000d * _options.AnimationSpeed;
        _scrollTransition = transition;
        _autoScrolling = true;
        static double BackgroundSpace(RowSlot row) => row.Backgrounds.Sum(back => back.ActualHeight + back.Margin.Top);
    }
    private void ManualScroll()
    {
        _pointerAtScroll = Mouse.GetPosition(_reference.View);
        _lastManualScroll = _wall.Elapsed.TotalSeconds;
        bool firstInput = _manualUntil <= _lastManualScroll;
        _manualUntil = _lastManualScroll + _options.AutoResumeSeconds;
        _autoScrolling = false;
        if (!firstInput)
            return;
        _dots.SuspendForManualScroll();
        for (int i = 0; i < PullCount; i++)
            AnimatePull(i, 0, 0, Motion.ScrollDurationMs(_options.AppleMusicLyrics, _pageKaraoke) * _options.AnimationSpeed, 0, (false, .2));
    }
    private void ExecuteMagic(MagicAction action)
    {
        switch (action)
        {
            case MagicAction.Close:
                if (_hostedByPlayer)
                    ShowLibrary();
                else
                    Close();
                break;
            case MagicAction.Maximize:
                if (_hostedByPlayer && !_fullscreen)
                    ToggleFullscreen();
                else
                    WindowState = WindowState.Maximized;
                _rebuildPending = true;
                break;
            case MagicAction.Restore:
                if (_fullscreen)
                    ToggleFullscreen();
                else
                    WindowState = WindowState.Normal;
                _rebuildPending = true;
                break;
            case MagicAction.Minimize:
                WindowState = WindowState.Minimized;
                break;
            case MagicAction.Top:
                ManualScroll();
                Follow(0);
                break;
            case MagicAction.Bottom:
                ManualScroll();
                Follow(_rows.Count - 1);
                break;
            case MagicAction.Current:
                _manualUntil = 0;
                _anchor = -2;
                Follow(Timeline.Anchor(_groups, _clock.PositionMs + _options.TimelineOffsetMs), _breathing);
                break;
        }
    }
    public async void Seek(double ms)
    {
        try
        {
            if (_system is { } system)
            {
                await system.SeekAsync(Math.Clamp(ms, 0, _clock.DurationMs));
                return;
            }
            if (_netEaseTracks.ContainsKey(_song.Id))
            {
                await ControlNetEaseAsync("time-pos", Math.Clamp(ms, 0, _clock.DurationMs) / 1000);
                return;
            }
            _clock.Seek(ms);
            _manualUntil = 0;
            _anchor = -2;
            _autoScrolling = false;
            if (_audioLoaded)
                _audio.Position = TimeSpan.FromMilliseconds(_clock.PositionMs);
        }
        catch (Exception e) { Notify(e.Message); App.Log(e.ToString()); }
    }
    public async void TogglePlay()
    {
        if (PlaybackLoading)
            return;
        try
        {
            if (!CanControlPlayback)
            {
                if (_system is null && _songs.Count > 0)
                    await NavigateLocalAsync(0, startPlaying: true);
                return;
            }
            if (_system is { } system)
            {
                if (_clock.IsPlaying)
                    await system.PauseAsync();
                else
                    await system.PlayAsync();
                return;
            }
            if (_netEaseTracks.TryGetValue(_song.Id, out var track))
            {
                if (_netEaseActive)
                    await ControlNetEaseAsync("pause", _netEasePlayRequested);
                else
                    await StartNetEaseAsync(track);
                return;
            }
            if (_clock.IsPlaying)
            {
                _clock.Pause();
                _audio.Pause();
            }
            else
            {
                if (_clock.PositionMs >= _clock.DurationMs)
                    _clock.Seek(0);
                _clock.Play();
                if (_audioLoaded)
                    _audio.Play();
            }
            UpdatePlayButtons();
        }
        catch (Exception e) { Notify(e.Message); App.Log(e.ToString()); }
    }
    private void UpdatePlayButtons()
    {
        UpdatePlaybackAvailability();
        bool playing = Playing;
        _reference.Get<ImageButton>("ImageClickablePauseButton").Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        _reference.Get<ImageButton>("ImageClickablePlayButton").Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        var shuffle = _reference.Get<ImageButton>("ImageClickableShuffleButton");
        var repeat = _reference.Get<ImageButton>("ImageClickableRepeatButton");
        shuffle.IconOpacity = _shuffle ? 1 : .3;
        repeat.IconOpacity = _repeat ? 1 : .3;
        shuffle.IsEnabled = CanShuffle;
        repeat.IsEnabled = CanRepeat;
        ToolTipService.SetShowOnDisabled(shuffle, true);
        ToolTipService.SetShowOnDisabled(repeat, true);
        shuffle.ToolTip = shuffle.IsEnabled ? "随机播放" : "此媒体会话未提供随机播放控制";
        repeat.ToolTip = repeat.IsEnabled ? "循环播放" : "此媒体会话未提供循环控制";
        bool one = _system is null ? _repeat : _remoteRepeatMode == MediaPlaybackAutoRepeatMode.Track;
        repeat.ImageSource = (ImageSource)Application.Current.Resources[one ? "Apple.Music.RepeatOneButton" : "Apple.Music.RepeatButton"];
    }
    internal async void ToggleShuffle()
    {
        try
        {
            if (_system is { } system)
            {
                await system.ShuffleAsync(!_shuffle);
                return;
            }
            _shuffle = !_shuffle;
            UpdatePlayButtons();
        }
        catch (Exception e) { Notify(e.Message); App.Log(e.ToString()); }
    }
    internal async void ToggleRepeat()
    {
        try
        {
            if (_system is { } system)
            {
                var mode = _remoteRepeatMode switch
                {
                    MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
                    MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
                    _ => MediaPlaybackAutoRepeatMode.None
                };
                await system.RepeatAsync(mode);
                return;
            }
            _repeat = !_repeat;
            UpdatePlayButtons();
        }
        catch (Exception e) { Notify(e.Message); App.Log(e.ToString()); }
    }
    private void UpdateArtworkScale()
    {
        bool playing = _clock.IsPlaying;
        double target = playing ? 1 : .75;
        if (_options.ReduceMotion)
        {
            _artworkScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _artworkScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _artworkScale.ScaleX = _artworkScale.ScaleY = target;
            _coverPlaying = playing;
            return;
        }
        if (_coverPlaying == playing)
            return;
        IEasingFunction easing = playing ? new BackEase { Amplitude = .7, EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseOut };
        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(400)) { EasingFunction = easing, BeginTime = TimeSpan.FromMilliseconds(_coverPlaying is null && playing ? 300 : 0) };
        _artworkScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        _artworkScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        _coverPlaying = playing;
    }
    internal async void Next()
    {
        try
        {
            if (_system is { } system)
            {
                await system.NextAsync();
                return;
            }
            if (_songs.Count == 0)
                return;
            if (_pendingQueueEntry is { } skipped)
                _playNext.Remove(skipped);
            await NavigateLocalAsync(NextQueueIndex());
        }
        catch (Exception e) { Notify(e.Message); App.Log(e.ToString()); }
    }
    internal async void Previous()
    {
        try
        {
            if (_system is { } system)
            {
                await system.PreviousAsync();
                return;
            }
            if (_clock.PositionMs > 3000)
            {
                Seek(0);
                return;
            }
            await NavigateLocalAsync(_songIndex - 1);
        }
        catch (Exception e) { Notify(e.Message); App.Log(e.ToString()); }
    }
    internal async Task NavigateLocalAsync(int index, bool startPlaying = false, CancellationToken token = default)
    {
        if (_songs.Count == 0 || _disposed)
            return;
        bool playing = startPlaying || (_navigationRequest != 0 ? _navigationPlaying : _clock.IsPlaying || _netEasePlayRequested);
        var requested = _songs[(index % _songs.Count + _songs.Count) % _songs.Count];
        long selection = ++_queueSelection;
        int generation = _loadGeneration + 1;
        _navigationRequest = selection;
        _navigationPlaying = playing;
        _pendingQueueEntry = requested;
        try
        {
            await LoadSongAsync(index, selection, token);
            if (_disposed || selection != _queueSelection || generation != _loadGeneration || _songIndex < 0 ||
                !ReferenceEquals(requested, _song) || token.IsCancellationRequested)
                return;
            if (_navigationPlaying)
            {
                if (_netEaseTracks.TryGetValue(_song.Id, out var track))
                    await StartNetEaseAsync(track, token);
                else
                {
                    _clock.Play();
                    if (_audioLoaded)
                        _audio.Play();
                }
            }
        }
        finally
        {
            if (_navigationRequest == selection)
            {
                _navigationRequest = 0;
                _navigationPlaying = false;
                _pendingQueueEntry = null;
            }
            if (!_disposed)
                UpdatePlayButtons();
        }
    }
    private void EndTrack()
    {
        if (!_clock.IsPlaying || _system is not null || PlaybackLoading)
            return;
        if (_repeat)
        {
            Seek(0);
            if (_audioLoaded)
                _audio.Play();
        }
        else
            Next();
    }
    internal void Favorite()
    {
        if (!_favorites.Add(_song.Id))
            _favorites.Remove(_song.Id);
        IsHearted = _favorites.Contains(_song.Id);
        Notify(IsHearted ? "已加入收藏" : "已取消收藏");
        RefreshQueue();
    }
    private void ToggleQueue()
    {
        _queue.Visibility = _queue.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        RefreshQueue();
    }
    private void RefreshQueue()
    {
        _queueItems.Children.Clear();
        if (_queue.Visibility != Visibility.Visible)
            return;
        for (int i = 0; i < _songs.Count; i++)
        {
            int index = i;
            var s = _songs[i];
            var item = Button(($"{(_favorites.Contains(s.Id) ? "♥  " : "")}{s.Title}\n{s.Artist}"), $"选择歌曲 {s.Title}", () => _ = LoadSongAsync(index), 230, 62);
            item.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (index == _songIndex)
                item.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            _queueItems.Children.Add(item);
        }
    }
    internal void Notify(string text)
    {
        _status.Text = text;
        _noticeUntil = _wall.Elapsed.TotalSeconds + 3;
    }
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) { Notify(e.Message); App.Log(e.ToString()); }
    }
    internal void OpenLyrics(Window? owner = null)
    {
        var dialog = new OpenFileDialog { Filter = "歌词|*.json;*.lrc;*.ttml;*.xml;*.txt|所有文件|*.*" };
        if (ShowFileDialog(dialog, owner))
            ImportLyrics(dialog.FileName, owner is not null && _song.Lines.Count == 0 ? _song.AudioPath : null);
    }
    public void ImportLyrics(string path) => ImportLyrics(path, null);
    private void ImportLyrics(string path, string? fallbackAudio)
    {
        Guard(() =>
        {
            var song = LyricsFile.Load(path, _options.UseLyricifyParser);
            if (string.IsNullOrWhiteSpace(song.AudioPath))
                song.AudioPath = fallbackAudio;
            _songs.Add(song);
            _ = LoadSongAsync(_songs.Count - 1);
        });
    }
    internal async void OpenAudio(Window? owner = null, bool newTrack = false)
    {
        var dialog = new OpenFileDialog { Filter = "音频|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac|所有文件|*.*" };
        if (!ShowFileDialog(dialog, owner))
            return;
        if (newTrack || _songIndex < 0)
            _ = AddAudioTrackAsync(dialog.FileName);
        else
        {
            try { await WhilePlaybackLoadsAsync(() => OpenAudioFileAsync(dialog.FileName)); }
            catch (Exception e) { Notify(e.Message); App.Log(e.ToString()); }
        }
    }
    private async Task OpenAudioFileAsync(string path)
    {
        if (_netEaseActive)
        {
            int generation = _loadGeneration;
            await StopNetEaseAsync();
            if (_disposed || generation != _loadGeneration)
                return;
        }
        _netEaseTracks.Remove(_song.Id);
        _system?.Dispose();
        _system = null;
        _clock.Pause();
        _clock.Seek(0);
        UpdatePlayButtons();
        _song.AudioPath = path;
        _audioOpening = _audioLoaded = true;
        try { _audio.Open(new Uri(Path.GetFullPath(path))); }
        catch
        {
            _audioOpening = _audioLoaded = false;
            throw;
        }
        _audio.Volume = _options.Volume;
        Notify(Path.GetFileName(path));
    }
    internal void OpenCover(Window? owner = null)
    {
        var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
        if (ShowFileDialog(dialog, owner))
        {
            var cover = Artwork.Load(dialog.FileName);
            _song.CoverPath = dialog.FileName;
            _hasExplicitArtwork = true;
            _artwork.Source = _smallArtwork.Source = cover;
            _ = _background.SetArtworkAsync(cover, _song.Id + dialog.FileName);
        }
    }
    private bool ShowFileDialog(OpenFileDialog dialog, Window? owner = null)
    {
        if (_fileDialogOpen || _wall.Elapsed.TotalSeconds < _ignoreDialogMouseUntil)
            return false;
        _fileDialogOpen = true;
        try
        {
            return dialog.ShowDialog(owner ?? this) == true;
        }
        finally { _fileDialogOpen = false; _ignoreDialogMouseUntil = _wall.Elapsed.TotalSeconds + .25; }
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            ImportLyrics(paths[0]);
    }
    internal async void ConnectSystemMedia()
    {
        var connection = new SystemMediaConnection(Dispatcher);
        try
        {
            if (_netEaseActive)
            {
                int generation = _loadGeneration;
                await StopNetEaseAsync();
                if (_disposed || generation != _loadGeneration)
                {
                    connection.Dispose();
                    return;
                }
            }
            _audio.Stop();
            _audioOpening = _audioLoaded = false;
            _system?.Dispose();
            _system = connection;
            PublishMediaPosition(0);
            _remoteShuffle = _remoteRepeat = _shuffle = _repeat = false;
            _remoteRepeatMode = null;
            UpdatePlayButtons();
            connection.Changed += snapshot => ApplySystemMediaSnapshot(connection, snapshot);
            connection.Disconnected += () => { if (!_disposed && ReferenceEquals(_system, connection)) { _clock.Pause(); _dots.StopPlayback(); UpdatePlayButtons(); Notify("等待系统媒体会话恢复"); } };
            connection.Error += message => { if (!_disposed && ReferenceEquals(_system, connection)) Notify(message); };
            await connection.ConnectAsync();
        }
        catch (Exception e) { connection.Dispose(); if (ReferenceEquals(_system, connection)) { _system = null; if (!_disposed) Notify(e.Message); } App.Log(e.ToString()); }
    }
    private void ApplySystemMediaSnapshot(SystemMediaConnection connection, MediaSnapshot snapshot)
    {
        if (_disposed || !ReferenceEquals(_system, connection))
            return;
        if (_songIndex < 0)
        {
            _song.Title = snapshot.Title;
            _song.Artist = snapshot.Artist;
        }
        else if (!string.Equals(snapshot.Title.Trim(), _song.Title.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _clock.Pause();
            _dots.StopPlayback();
            UpdatePlayButtons();
            Notify("播放器曲目与歌词不匹配，请导入对应歌词。");
            return;
        }
        if (snapshot.DurationMs is > 0 and <= 86_400_000 && Math.Abs(snapshot.DurationMs - _clock.DurationMs) > .5)
        {
            double p = _clock.PositionMs;
            _clock.Load(snapshot.DurationMs);
            _clock.Seek(p);
        }
        if (Math.Abs(snapshot.PositionMs - _clock.PositionMs) > 120)
            _clock.Seek(snapshot.PositionMs);
        _shuffle = snapshot.ShuffleActive == true;
        _remoteRepeatMode = snapshot.RepeatMode;
        _repeat = snapshot.RepeatMode is { } mode && mode != MediaPlaybackAutoRepeatMode.None;
        _remoteShuffle = snapshot.CanShuffle;
        _remoteRepeat = snapshot.CanRepeat;
        if (snapshot.Playing)
            _clock.Play();
        else
            _clock.Pause();
        _title.Text = _smallTitle.Text = _song.Title;
        _artist.Text = _smallArtist.Text = string.IsNullOrWhiteSpace(_song.Artist) ? snapshot.Artist : _song.Artist;
        UpdatePlayButtons();
        if (snapshot.Artwork is { } artwork && !ReferenceEquals(_mediaArtwork, artwork))
            _ = UpdateMediaArtworkAsync(connection, artwork);
    }
    private async Task UpdateMediaArtworkAsync(SystemMediaConnection connection, byte[] bytes)
    {
        if (_disposed || !ReferenceEquals(_system, connection) || ReferenceEquals(_mediaArtwork, bytes))
            return;
        _mediaArtwork = bytes;
        if (_hasExplicitArtwork)
            return;
        try
        {
            var cover = Artwork.Decode(bytes);
            _artwork.Source = _smallArtwork.Source = cover;
            await _background.SetArtworkAsync(cover, _song.Id + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        }
        catch (Exception e) { if (!_disposed && ReferenceEquals(_system, connection)) Notify("媒体封面无法读取：" + e.Message); App.Log(e.ToString()); }
    }
    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _restoreBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _restoreState = WindowState;
            WindowState = WindowState.Normal;
            _fullscreen = true;
            UpdateWindowChrome();
            WindowState = WindowState.Maximized;
        }
        else
        {
            _fullscreen = false;
            WindowState = WindowState.Normal;
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            WindowState = _restoreState;
        }
        _rebuildPending = true;
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_hostedByPlayer && !_lyricsShown)
        {
            if (HandleLibraryKey(e))
                e.Handled = true;
            return;
        }
        if (HandleKey(e.Key, Keyboard.Modifiers, e.OriginalSource is TextBox))
            e.Handled = true;
    }
    private bool HandleKey(Key key, ModifierKeys modifiers, bool textInput = false)
    {
        if (textInput)
            return false;
        bool ctrl = (modifiers & ModifierKeys.Control) != 0, alt = (modifiers & ModifierKeys.Alt) != 0;
        if (key == Key.Space)
            TogglePlay();
        else if (key == Key.F11)
            ToggleFullscreen();
        else if (ctrl && key == Key.T)
        {
            _options.Topmost = !_options.Topmost;
            Topmost = _options.Topmost;
        }
        else if (ctrl && key is Key.Add or Key.OemPlus)
            ChangeZoom(true);
        else if (ctrl && key is Key.Subtract or Key.OemMinus)
            ChangeZoom(false);
        else if (ctrl && key == Key.D0)
            ResetZoom();
        else if (alt && key == Key.C)
        {
            _options.Translation = !_options.Translation;
            _rebuildPending = true;
        }
        else if (alt && key == Key.R)
        {
            _options.Romanization = !_options.Romanization;
            _rebuildPending = true;
        }
        else if (key == Key.Escape)
        {
            if (_queue.Visibility == Visibility.Visible)
                _queue.Visibility = Visibility.Collapsed;
            else if (_fullscreen)
                ToggleFullscreen();
            else if (_hostedByPlayer)
                ShowLibrary();
        }
        else
            return false;
        return true;
    }
    private void ChangeZoom(bool zoomIn)
    {
        SetZoom(ZoomRules.AdjustInverse(_inverseZoom, zoomIn));
    }
    private void ResetZoom()
    {
        bool fullscreen = _fullscreen || WindowState == WindowState.Maximized;
        double height = _reference.View.ActualHeight;
        bool tall = height / Math.Max(1, _reference.View.ActualWidth) > 1;
        SetZoom(ZoomRules.DefaultInverse(tall, fullscreen, height));
    }
    private void UpdateZoomContext(bool portrait, bool maximized, double height)
    {
        var settings = _options.AppleMusicLyrics;
        var levels = settings.ZoomingLevels;
        int context = (portrait ? 1 : 0) + (maximized ? 2 : 0);
        double saved = ZoomRules.Remembered(levels, portrait, maximized);
        if (context != _zoomContext || _zoomSettings != settings || _zoomLevels != levels || _zoomRestore != settings.ZoomingRestore || !saved.Equals(_rememberedZoom))
        {
            bool legacy = _zoomContext < 0 && settings.ZoomingRestore && _options.Zoom != 1 && new[] { levels.Horizontal, levels.Vertical, levels.HorizontalFullscreen, levels.VerticalFullscreen }.All(double.IsNaN);
            _inverseZoom = ZoomRules.Restore(settings, portrait, maximized, height);
            if (legacy)
            {
                _inverseZoom /= _options.Zoom;
                ZoomRules.Remember(settings, portrait, maximized, _inverseZoom);
            }
            _zoomContext = context;
            _zoomSettings = settings;
            _zoomLevels = levels;
            _zoomRestore = settings.ZoomingRestore;
            _rememberedZoom = ZoomRules.Remembered(levels, portrait, maximized);
        }
        _options.Zoom = ZoomRules.DefaultInverse(portrait, maximized, height) / _inverseZoom;
    }
    private void SetZoom(double inverse)
    {
        _inverseZoom = inverse;
        bool maximized = _fullscreen || WindowState == WindowState.Maximized;
        ZoomRules.Remember(_options.AppleMusicLyrics, _portrait, maximized, inverse);
        _rememberedZoom = ZoomRules.Remembered(_options.AppleMusicLyrics.ZoomingLevels, _portrait, maximized);
        _options.Zoom = ZoomRules.DefaultInverse(_portrait, maximized, _reference.View.ActualHeight) / inverse;
        App.SaveOptions(_options);
        _rebuildPending = true;
    }
    private ContextMenu BuildContextMenu()
    {
        var menu = _reference.Get<ContextMenu>("MenuRightClick");
        ReferenceMenus.Apply(menu);
        Action[] commands = [() => { _options.Topmost = !_options.Topmost; Topmost = _options.Topmost; }, () => ChangeZoom(true), () => ChangeZoom(false), ResetZoom, () => { _options.Blur = !_options.Blur; _rebuildPending = true; }, () => { _options.Bold = !_options.Bold; _rebuildPending = true; }, () => { _options.Translation = !_options.Translation; _rebuildPending = true; }];
        var originalItems = menu.Items.OfType<MenuItem>().ToArray();
        if (originalItems.Length != commands.Length)
            throw new InvalidOperationException("Reference context menu changed.");
        for (int i = 0; i < commands.Length; i++)
        {
            int command = i;
            originalItems[i].Click += (_, _) => Guard(() => { commands[command](); RefreshContextMenu(); });
        }
        menu.Opened += (_, _) => RefreshContextMenu();
        RefreshContextMenu();
        menu.Items.Add(new Separator());
        Add("导入歌词…", () => OpenLyrics());
        Add("打开本地音频…", () => OpenAudio());
        Add("连接系统媒体", ConnectSystemMedia);
        menu.Items.Add(new Separator());
        Add("Apple Music 歌词设置…", OpenLyricsSettings);
        Add("播放列表", ToggleQueue);
        Add("全屏  F11", ToggleFullscreen);
        Add("返回当前歌词", () => { _manualUntil = 0; _anchor = -2; });
        if (_hostedByPlayer)
            Add("返回资料库  Esc", ShowLibrary);
        else
            Add("关闭", Close);
        return menu;
        void Add(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => Guard(action);
            menu.Items.Add(item);
        }
    }
    private void RefreshContextMenu()
    {
        foreach (var (name, enabled) in new[] { ("MenuItemTopmost", _options.Topmost), ("MenuItemBlur", _options.Blur), ("MenuItemBoldText", _options.Bold), ("MenuItemTranslation", _options.Translation) })
            _reference.Get<IconMenuItem>(name).SymbolIcon = enabled ? "Checkmark12" : "Empty";
    }
    private void ConnectTrackMenus()
    {
        var menu = _reference.Get<ContextMenu>("MenuThreeDot");
        ReferenceMenus.Apply(menu);
        _reference.Get<MenuItem>("MenuItemAddToLibrary").Click += (_, _) => Favorite();
        _reference.Get<MenuItem>("MenuItemDeleteFromLibrary").Click += (_, _) => Favorite();
        _reference.Get<MenuItem>("MenuItemOpenTrackManagement").Click += (_, _) => ToggleQueue();
        foreach (var item in menu.Items.OfType<MenuItem>().Where(i => i.Name is not ("MenuItemAddToLibrary" or "MenuItemDeleteFromLibrary" or "MenuItemOpenTrackManagement")))
        {
            item.IsEnabled = false;
            item.ToolTip = "当前播放源未提供跳转信息";
        }
        var artist = _reference.Get<ContextMenu>("MenuArtistNameClick");
        ReferenceMenus.Apply(artist);
        foreach (var item in artist.Items.OfType<MenuItem>())
        {
            item.IsEnabled = false;
            item.ToolTip = "当前播放源未提供跳转信息";
        }
        foreach (var name in new[] { "TextBlockArtist", "TextBlockVerticalModeArtistName" })
            _reference.Get<TextBlock>(name).MouseLeftButtonUp += (sender, _) => { RefreshTrackMenu(); artist.PlacementTarget = (UIElement)sender; artist.IsOpen = true; };
    }
    private void RefreshTrackMenu()
    {
        _reference.Get<MenuItem>("MenuItemAddToLibrary").Visibility = IsHearted ? Visibility.Collapsed : Visibility.Visible;
        _reference.Get<MenuItem>("MenuItemDeleteFromLibrary").Visibility = IsHearted ? Visibility.Visible : Visibility.Collapsed;
        _reference.Get<TextBlock>("TextBlockGoToArtistContent").Text = _song.Artist;
        _reference.Get<TextBlock>("TextBlockGoToAlbumContent").Text = _song.Album;
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        if (_disposed)
            return;
        _disposed = true;
            _queueLifetime.Cancel();
        CompositionTarget.Rendering -= OnRendering;
        if (_netEaseActive)
            _ = StopNetEaseAsync();
        _clock.Pause();
        _system?.Dispose();
        _publishedMedia?.Dispose();
        _audio.Close();
        StopArtworkVideo();
        _dots.Dispose();
        _magicController.Dispose();
        _blend.Dispose();
        _background.Dispose();
        ReleaseSleepRequest();
        try
        {
            App.SaveOptions(_options);
        }
        catch (Exception ex) { App.Log(ex.ToString()); }
    }
    private void UpdateSleepRequest()
    {
        if (!_sleepPrevention.Update(IsLoaded && !_disposed && (!_hostedByPlayer || LyricsVisible), _options.AppleMusicLyrics.PreventSleepFullscreenOnly, WindowState))
            App.Log("Windows rejected the thread execution-state request.");
    }
    private void ReleaseSleepRequest()
    {
        if (!_sleepPrevention.Release())
            App.Log("Windows rejected restoring the thread execution state.");
    }
    public object State() => new { title = _song.Title, positionMs = _clock.PositionMs, durationMs = _clock.DurationMs, playing = _clock.IsPlaying, generation = _clock.Generation, anchor = _anchor, portrait = _portrait, rows = _rows.Count, frames = _frameCount, background = _options.Background.ToString(), backgroundError = _background.LastError };
    public void Capture(string path)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_root);
        File.WriteAllBytes(path, Artwork.Encode(bitmap));
    }

}
