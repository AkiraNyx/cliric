using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
using LyricReplica.Core;
using Timeline = LyricReplica.Core.Timeline;
using LyricReplica.App.Reference;

namespace LyricReplica.App;

public sealed partial class MainWindow : Window
{
    public static readonly DependencyProperty IsHeartedProperty=DependencyProperty.Register(nameof(IsHearted),typeof(bool),typeof(MainWindow),new PropertyMetadata(false));
    public bool IsHearted{get=>(bool)GetValue(IsHeartedProperty);set=>SetValue(IsHeartedProperty,value);}
    private sealed record RowSlot(DisplayLine Data,StackPanel Host,LyricRow Main,IReadOnlyList<LyricRow> Backgrounds){public TranslateTransform Pull{get;}=new();}
    private ViewOptions _options=App.LoadOptions();
    private readonly PlaybackClock _clock=new();
    private readonly MediaPlayer _audio=new();
    private readonly BackgroundView _background=new();
    private readonly SleepPrevention _sleepPrevention;
    private readonly Grid _root=new();
    private Grid _stage=new(),_lyricHost=new();
    private ReferencePageView _reference=null!;
    private PlusLighterComposer _blend=null!;
    private double _displayScale=1;
    private double _inverseZoom=1.1,_rememberedZoom=double.NaN;
    private int _zoomContext=-1;
    private AppleMusicSettings? _zoomSettings;
    private ZoomLevels? _zoomLevels;
    private bool _zoomRestore;
    private readonly ScaleTransform _artworkScale=new(.75,.75);
    private bool? _coverPlaying;
    private double _legacyTime;
    private ColumnDefinition _leftColumn=new(){Width=new GridLength(.82,GridUnitType.Star)};
    private ScrollViewer _scroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false,PanningMode=PanningMode.VerticalOnly};
    private Panel _linesPanel=new StackPanel();
    private readonly List<RowSlot> _rows=[];
    private Viewbox _infoView=new(){Stretch=Stretch.Uniform,Margin=new Thickness(0,70,0,60)};
    private FrameworkElement _smallInfo=new Grid();
    private Grid _verticalControls=new();
    private Image _artwork=new(){Stretch=Stretch.UniformToFill},_smallArtwork=new(){Stretch=Stretch.UniformToFill};
    private TextBlock _title=new(),_artist=new();
    private TextBlock _smallTitle=new(),_smallArtist=new(),_status=new(),_fps=new();
    private BreathingDots _dots=new();
    private ReferenceStateBar _seek=new(),_seekVertical=new();
    private readonly Border _queue=new(),_settings=new();
    private FrameworkElement _magic=new Border();
    private UserControl? _credit;
    private MagicStripController _magicController=null!;
    private readonly List<ButtonDragFeedback> _buttonDrags=[];
    private readonly StackPanel _queueItems=new();
    private readonly Stopwatch _wall=Stopwatch.StartNew();
    private readonly string[] _args;
    private bool _fileDialogOpen;
    private double _ignoreDialogMouseUntil;
    private readonly List<Song> _songs=DemoSongs.Create();
    private List<DisplayLine> _groups=[];
    private readonly HashSet<string> _favorites=[];
    private Song _song=new();
    private int _songIndex,_anchor=-2,_frameCount,_lastFpsFrames,_loadGeneration;
    private int _navigationRequest;
    private bool _navigationPlaying;
    private bool _resetWordRuns=true;
    private long _renderedGeneration=-1;
    private double _lastWall,_lastFps,_manualUntil,_scrollFrom,_scrollTo,_scrollStart,_scrollDuration,_noticeUntil;
    private bool _portrait,_fullscreen,_disposed,_rebuildPending=true,_autoScrolling,_shuffle,_repeat,_audioLoaded,_suppressMediaEnded,_verticalControlsShown,_breathing,_pageKaraoke;
    private Point _touchStart;
    private Point? _pointerAtScroll;
    private Rect _restoreBounds;
    private WindowState _restoreState;
    private SystemMediaConnection? _system;
    private bool _remoteShuffle,_remoteRepeat;
    private MediaPlaybackAutoRepeatMode? _remoteRepeatMode;
    private byte[]? _mediaArtwork;
    private bool _hasExplicitArtwork;
    private bool _updatingWindowChrome;
    private bool _independentPresentation;
    private FontFamily _font=new("Segoe UI");
    private readonly int _singOpacityDuration;
    public int RenderedFrameCount=>_frameCount;
    public Song CurrentSong=>_song;
    public PlaybackClock Clock=>_clock;
    public ViewOptions Options=>_options;
    public bool Portrait=>_portrait;
    public string? BackgroundError=>_background.LastError;

    public MainWindow(string[] args)
    {
        _args=args;
        _sleepPrevention=args.Contains("--verify-offscreen")?new(_=>throw new InvalidOperationException("Offscreen verification must not call the system execution-state API.")):new();
        if(args.Contains("--self-test")||args.Contains("--verify-offscreen")){_options=new();_options.Background=BackgroundMode.Legacy;_options.ReduceMotion=true;}
        _singOpacityDuration=_options.AppleMusicLyrics.LineSwitchingAppleIosStyle164?700:350;
        Title="LyricReplica · Apple Music Lyrics";Width=1240;Height=820;MinWidth=490;MinHeight=520;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;
        Background=Brushes.Black;Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
        WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(5),CornerRadius=new CornerRadius(0)});
        Topmost=_options.Topmost;
        ReferenceMenus.UpdateTheme(_options.LightTheme);BuildResources();BuildPage();
        _background.Ready+=()=>UpdateIndependentPresentation(_background.IndependentRendererActive&&_background.RendererReady);
        _background.Fault+=message=>{UpdateIndependentPresentation(false);App.Log("Background: "+message);Notify("背景渲染器切换到兼容模式");};
        _audio.MediaOpened+=(_,_)=>
        {
            if(_audio.NaturalDuration.HasTimeSpan)
            {
                double p=_clock.PositionMs;bool play=_clock.IsPlaying;
                _song.DurationMs=_audio.NaturalDuration.TimeSpan.TotalMilliseconds;_clock.Load(_song.DurationMs);_clock.Seek(p);if(play)_clock.Play();
            }
        };
        _audio.MediaEnded+=(_,_)=>{if(!_suppressMediaEnded)EndTrack();};
        _audio.MediaFailed+=(_,e)=>{_audioLoaded=false;Notify("音频无法播放："+e.ErrorException.Message);App.Log(e.ErrorException.ToString());};
        Loaded+=OnLoaded;SizeChanged+=(_,_)=>_rebuildPending=true;
        StateChanged+=(_,_)=>{if(_updatingWindowChrome)return;UpdateWindowChrome();_rebuildPending=true;UpdateSleepRequest();};
        Unloaded+=(_,_)=>ReleaseSleepRequest();
        PreviewKeyDown+=OnKey;Closed+=OnClosed;
        PreviewMouseUp+=(_,e)=>{if(_wall.Elapsed.TotalSeconds<_ignoreDialogMouseUntil)e.Handled=true;};
        AllowDrop=true;Drop+=OnDrop;
    }

    private void BuildResources()
    {
        var style=new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));style.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8)));
        style.Setters.Add(new Setter(Control.FontSizeProperty,14d));style.Setters.Add(new Setter(Control.CursorProperty,Cursors.Hand));
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)});
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(presenter);style.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(Button)){VisualTree=border}));
        var hover=new Trigger{Property=IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromArgb(28,255,255,255))));style.Triggers.Add(hover);
        _root.Resources.Add(typeof(Button),style);
    }
    private void UpdateWindowChrome()
    {
        if(_updatingWindowChrome)return;
        bool maximized=WindowState==WindowState.Maximized;
        WindowChrome.GetWindowChrome(this).ResizeBorderThickness=new Thickness(maximized?0:5);
        bool fixedSize=maximized&&_options.Background==BackgroundMode.Modern&&_options.AppleMusicLyrics.EnableIndependentBackgroundRenderer;
        if(fixedSize&&ResizeMode!=ResizeMode.NoResize)
        {
            _updatingWindowChrome=true;
            try{WindowState=WindowState.Normal;ResizeMode=ResizeMode.NoResize;WindowState=WindowState.Maximized;}
            finally{_updatingWindowChrome=false;}
        }
        else ResizeMode=fixedSize?ResizeMode.NoResize:ResizeMode.CanResize;
        if(ResizeMode!=ResizeMode.NoResize)
        {
            if(WindowState==WindowState.Normal){_root.Margin=new Thickness(0);Topmost=_options.Topmost;}
            else if(WindowState==WindowState.Maximized){_root.Margin=new Thickness(6);Topmost=false;}
        }
        UpdateIndependentPresentation(_background.IndependentRendererActive&&_background.RendererReady);
    }
    private void UpdateIndependentPresentation(bool ready)
    {
        if(_disposed)return;
        bool transparent=ready&&WindowState==WindowState.Maximized&&_options.Background==BackgroundMode.Modern&&_options.AppleMusicLyrics.EnableIndependentBackgroundRenderer&&!_options.ReduceMotion;
        if(_independentPresentation==transparent)return;
        _independentPresentation=transparent;
        Background=_background.Background=transparent?Brushes.Transparent:Brushes.Black;
        var chrome=WindowChrome.GetWindowChrome(this);
        if(transparent)chrome.GlassFrameThickness=new Thickness(-1);
        else chrome.ClearValue(WindowChrome.GlassFrameThicknessProperty);
        if(PresentationSource.FromVisual(this) is HwndSource source&&source.CompositionTarget is { } target)
            target.BackgroundColor=transparent?Colors.Transparent:Colors.Black;
    }
    private Button Button(string label,string automation,Action action,double width=36,double height=36)
    {
        var b=new Button{Content=label,Width=width,Height=height,ToolTip=automation,Margin=new Thickness(2)};
        AutomationProperties.SetName(b,automation);b.Click+=(_,_)=>Guard(action);return b;
    }
    private void BuildPage()
    {
        Content=_root;_root.Children.Add(_background);
        var family=ReferencePageView.FontFor(FontPresets.SpotifyMix);
        _reference=new ReferencePageView(family,_args.Contains("--verify-offscreen"));_root.Children.Add(_reference.View);
        _blend=new PlusLighterComposer(_reference);
        _reference.View.DataContext=this;
        _stage=_reference.Get<Grid>("MainGrid");_leftColumn=_reference.Get<ColumnDefinition>("ColumnDefinitionLeftSide");
        _lyricHost=_reference.Get<Grid>("GridLyrics");_scroll=_reference.Get<ScrollViewer>("ScrollViewerLyrics");_linesPanel=_reference.Get<Panel>("StackPanelLyrics");
        _infoView=_reference.Get<Viewbox>("ViewboxSongInfo");_smallInfo=_reference.Get<Viewbox>("ViewboxSongInfoSmall");_verticalControls=_reference.Get<Grid>("GridVerticalControls");
        _artwork=_reference.Get<ArtworkImage>("WebImageAlbum");_smallArtwork=_reference.Get<ArtworkImage>("WebImageAlbumSmall");
        InitializeArtworkVideo();
        _title=_reference.Get<TextBlock>("TextBlockTrackName");_artist=_reference.Get<TextBlock>("TextBlockArtist");
        _smallTitle=_reference.Get<TextBlock>("TextBlockVerticalModeTrackName");_smallArtist=_reference.Get<TextBlock>("TextBlockVerticalModeArtistName");
        _seek=_reference.Get<ReferenceStateBar>("AppleMusicPlayerStateBar");_seekVertical=_reference.Get<ReferenceStateBar>("AppleMusicPlayerStateBarVertical");
        _dots=_reference.Get<BreathingDots>("AppleMusicBreathingDots");_magic=_reference.Get<Border>("BorderMagicButtonOutside");
        if(!_args.Contains("--verify-offscreen"))_dots.ConfigurePlayback(()=>_clock.PositionMs+_options.TimelineOffsetMs,()=>_clock.IsPlaying,()=>_wall.Elapsed.TotalMilliseconds);
        _fps=_reference.Get<TextBlock>("TextBlockFps");
        _seek.SeekRequested+=Seek;_seekVertical.SeekRequested+=Seek;
        foreach(var button in _reference.Elements.OfType<ImageButton>())
        {
            string handler=InputMap.GetClick(button);
            button.Click+=(_,_)=>Guard(()=>
            {
                switch(handler)
                {
                    case "ImageClickablePauseButton_Click":case "ImageClickablePlayButton_Click":TogglePlay();break;
                    case "ImageClickablePrevious_Click":Previous();break;
                    case "ImageClickableNext_Click":Next();break;
                    case "ImageClickableShuffle_Click":ToggleShuffle();break;
                    case "ImageClickableRepeat_Click":ToggleRepeat();break;
                }
            });
        }
        foreach(var favorite in _reference.Elements.OfType<FavoriteButton>())favorite.Click+=(_,_)=>Favorite();
        foreach(var name in new[]{"GridThreeDotMenu","GridThreeDotMenuVerticalMode"})_reference.Get<Grid>(name).MouseLeftButtonUp+=(sender,_)=>{RefreshTrackMenu();var menu=_reference.Get<ContextMenu>("MenuThreeDot");menu.PlacementTarget=(UIElement)sender;menu.IsOpen=true;};
        foreach(var (hitName,targetName) in new[]{("BorderFavoriteButtonMainForMousePoint","FavoriteButtonMain"),("BorderFavoriteButtonVerticalModeForMousePoint","FavoriteButtonVerticalMode"),("BorderMenuBackgroundForMousePoint","GridThreeDotMenu"),("BorderMenuVerticalModeBackgroundForMousePoint","GridThreeDotMenuVerticalMode")})
        {
            var hit=_reference.Get<Border>(hitName);var target=_reference.Get<FrameworkElement>(targetName);var menu=_reference.Get<ContextMenu>("MenuThreeDot");
            _buttonDrags.Add(new(hit,target,()=>_options.AppleMusicLyrics.EnableAdvancedButtonDragAnimation,()=>!_options.ReduceMotion,()=>
            {if(target is FavoriteButton favorite)favorite.Activate();else{RefreshTrackMenu();menu.PlacementTarget=hit;menu.IsOpen=true;}},target is FavoriteButton heart?heart.SetPressedVisual:null,()=>menu.IsOpen));
        }
        _reference.Get<ContextMenu>("MenuThreeDot").Closed+=(_,_)=>{foreach(var drag in _buttonDrags)drag.Reset();};
        _reference.Get<Grid>("GridArtwork").RenderTransform=_artworkScale;
        _artwork.MouseLeftButtonUp+=(_,_)=>OpenCover();
        _scroll.PreviewMouseWheel+=(_,e)=>{SetVerticalControls(e.Delta>0);ManualScroll();};
        _scroll.PreviewTouchDown+=(_,e)=>{_touchStart=e.GetTouchPoint(_scroll).Position;ManualScroll();};
        _scroll.PreviewTouchMove+=(_,e)=>{double delta=e.GetTouchPoint(_scroll).Position.Y-_touchStart.Y;if(delta>150)SetVerticalControls(true);else if(delta< -150)SetVerticalControls(false);};
        _lyricHost.SizeChanged+=(_,_)=>_rebuildPending=true;
        _magicController=new MagicStripController(_reference,()=>WindowState==WindowState.Maximized,()=>_options.ReduceMotion,ExecuteMagic,()=>_options.AppleMusicLyrics,()=>Guard(()=>App.SaveOptions(_options)));
        _reference.Get<Grid>("GridMagicButton").MouseLeftButtonDown+=(_,e)=>{if(!e.Handled&&e.ClickCount==1&&e.LeftButton==MouseButtonState.Pressed)Guard(DragMove);};
        _status.FontSize=10;_status.Foreground=Brushes.White;_status.Opacity=.45;_status.Margin=new Thickness(18,0,18,12);_status.VerticalAlignment=VerticalAlignment.Bottom;_status.IsHitTestVisible=false;_status.Visibility=Visibility.Collapsed;_root.Children.Add(_status);
        ConfigureOverlay(_queue,290);var queueDock=new DockPanel();_queue.Child=queueDock;
        var queueHeader=new TextBlock{Text="播放列表",Foreground=Brushes.White,FontSize=18,Margin=new Thickness(12)};DockPanel.SetDock(queueHeader,Dock.Top);queueDock.Children.Add(queueHeader);queueDock.Children.Add(new ScrollViewer{Content=_queueItems,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});_root.Children.Add(_queue);
        ConfigureOverlay(_settings,335);_root.Children.Add(_settings);ContextMenu=BuildContextMenu();ConnectTrackMenus();
    }
    private static void ConfigureOverlay(Border border,double width)
    {border.Width=width;border.HorizontalAlignment=HorizontalAlignment.Right;border.VerticalAlignment=VerticalAlignment.Stretch;border.Margin=new Thickness(15,45,15,20);border.Padding=new Thickness(14);border.CornerRadius=new CornerRadius(16);border.Background=new SolidColorBrush(Color.FromArgb(244,28,29,40));border.Visibility=Visibility.Collapsed;border.BorderBrush=new SolidColorBrush(Color.FromArgb(30,255,255,255));border.BorderThickness=new Thickness(1);}

    private async void OnLoaded(object sender,RoutedEventArgs e)
    {
        UpdateSleepRequest();
        _audio.Volume=_options.Volume;
        await LoadSongAsync(0);
        if(_disposed)return;
        _clock.Seek(8500);
        _magicController.BeginTutorial(_wall.Elapsed.TotalMilliseconds);
        CompositionTarget.Rendering+=OnRendering;
        if(_args.Length>0&&!_args[0].StartsWith("--"))ImportLyrics(_args[0]);
        if(_args.Contains("--self-test"))await RunSelfTestAsync();
    }
    public async Task LoadSongAsync(int index)
    {
        if(_disposed)return;
        _dots.StopPlayback();
        StopArtworkVideo();
        int generation=++_loadGeneration;
        if(_navigationRequest!=generation){_navigationRequest=0;_navigationPlaying=false;}
        _seek.CancelPreview();_seekVertical.CancelPreview();
        _system?.Dispose();_system=null;
        _mediaArtwork=null;
        _suppressMediaEnded=true;_audio.Stop();_audio.Close();_audioLoaded=false;_suppressMediaEnded=false;
        _songIndex=(index%_songs.Count+_songs.Count)%_songs.Count;_song=_songs[_songIndex];_scroll.ScrollToVerticalOffset(0);_autoScrolling=false;
        IsHearted=_favorites.Contains(_song.Id);
        foreach(var favorite in _reference.Elements.OfType<FavoriteButton>())favorite.ResetVisual();
        _clock.Load(_song.DurationMs);_anchor=-2;_manualUntil=0;_resetWordRuns=true;
        _title.Text=_smallTitle.Text=_song.Title;_artist.Text=_smallArtist.Text=_song.Artist;
        BitmapSource cover;_hasExplicitArtwork=false;
        try
        {
            if(!string.IsNullOrWhiteSpace(_song.CoverPath)&&File.Exists(_song.CoverPath)){cover=Artwork.Load(_song.CoverPath);_hasExplicitArtwork=true;}
            else cover=Artwork.Create(_songIndex);
        }
        catch(Exception ex){App.Log(ex.ToString());cover=Artwork.Create(_songIndex);}
        _artwork.Source=_smallArtwork.Source=cover;
        await _background.SetModeAsync(_options.Background,_options);
        if(generation!=_loadGeneration||_disposed)return;
        await _background.SetArtworkAsync(cover,_song.Id);
        if(generation!=_loadGeneration||_disposed)return;
        if(!string.IsNullOrWhiteSpace(_song.AudioPath)&&File.Exists(_song.AudioPath))OpenAudioFile(_song.AudioPath);
        _rebuildPending=true;RefreshQueue();UpdatePlayButtons();
    }
    private void OnRendering(object? sender,EventArgs e)
    {
        if(_disposed)return;
        double now=_wall.Elapsed.TotalSeconds,dt=Math.Clamp(now-_lastWall,0,.1);_lastWall=now;_frameCount++;
        _magicController.AdvanceTutorial(now*1000);
        _status.Visibility=now<_noticeUntil?Visibility.Visible:Visibility.Collapsed;
        if(_rebuildPending&&_reference.View.ActualWidth>50){_rebuildPending=false;RebuildRows();}
        double position=_clock.PositionMs;
        if(_clock.IsPlaying&&position>=_clock.DurationMs-.1&&_clock.DurationMs>0){EndTrack();position=_clock.PositionMs;}
        if(_audioLoaded&&_clock.IsPlaying&&Math.Abs(_audio.Position.TotalMilliseconds-position)>150)_clock.Seek(_audio.Position.TotalMilliseconds);
        _seek.IsRealTimeUpdate=_seekVertical.IsRealTimeUpdate=_options.RealtimeSeek;
        _seek.ReduceMotion=_seekVertical.ReduceMotion=_options.ReduceMotion;
        _seek.AnimatedTime=_seekVertical.AnimatedTime=_options.AnimatedTime;
        _seek.SetPosition(position,_clock.DurationMs);_seekVertical.SetPosition(position,_clock.DurationMs);
        double lyricTime=position+_options.TimelineOffsetMs;
        double wordTime=lyricTime-LyricsPreparation.TimelineCompensationMs;
        double? animationTime=_args.Contains("--verify-offscreen")?null:now*1000;
        int anchor=Timeline.Anchor(_groups,lyricTime);
        bool discontinuity=_renderedGeneration!=_clock.Generation;
        if(discontinuity)foreach(var row in _rows){row.Pull.BeginAnimation(TranslateTransform.YProperty,null);row.Pull.Y=0;}
        bool slotChange=_rows.Any(r=>r.Backgrounds.Any(b=>b.SlotOpen!=Timeline.Active(r.Data,lyricTime)));
        CapturePullLayout();ConfigureSwitchDelays(anchor,lyricTime,now);
        double[]? previous=slotChange&&!discontinuity&&!_options.ReduceMotion?_rows.Select(r=>VisualTreeHelper.GetOffset(r.Host).Y+r.Pull.Y).ToArray():null;
        for(int i=0;i<_rows.Count;i++)
        {
            var row=_rows[i];bool active=Timeline.Active(row.Data,lyricTime);
            double start=row.Data.Untimed?double.PositiveInfinity:row.Data.StartMs;
            foreach(var backing in row.Backgrounds)backing.Update(wordTime,lyricTime,active,i-anchor,start,row.Data.EndMs,_clock.Generation,animationTime);
            row.Main.Update(wordTime,lyricTime,active,i-anchor,start,row.Data.EndMs,_clock.Generation,animationTime);
        }
        if(_credit is not null)((StackPanel)_credit.FindName("StackPanelLyrics")).Opacity=anchor>=_rows.Count?1:_options.Blur?.4:.3;
        if(slotChange)
        {
            _linesPanel.UpdateLayout();
            if(previous is not null&&(!_options.AppleMusicLyrics.PullingEffect||now<_manualUntil))
                for(int i=0;i<_rows.Count;i++)
                {
                    var row=_rows[i];double displacement=previous[i]-VisualTreeHelper.GetOffset(row.Host).Y-row.Pull.Y;
                    if(Math.Abs(displacement)<.1)continue;
                    AnimatePull(i,displacement,0,Motion.ScrollDurationMs(_options.AppleMusicLyrics,_pageKaraoke)*_options.AnimationSpeed,(int)Motion.PullingDelayMs(Math.Max(0,i-anchor),_options.AnimationSpeed,_options.AppleMusicLyrics.PullingDelay,_options.AppleMusicLyrics.InterLineAnimationPreset==InterLineAnimationPresetTypes.iOS,row.Main.OpacityTransition.UseBackEase),row.Main.OpacityTransition);
                }
        }
        _renderedGeneration=_clock.Generation;
        var gap=Timeline.BreathingGap(_groups,position+_options.TimelineOffsetMs);
        if(gap is { } breathing&&!_song.Untimed)
        {
            var nextLine=_groups.FirstOrDefault(x=>x.StartMs>=breathing.End);
            _dots.SetAlignment(_options.Duet?(nextLine?.Main.AlignLeft==false?HorizontalAlignment.Right:HorizontalAlignment.Left):null,_options.AppleMusicLyrics);
            if(animationTime is null)_dots.Advance(position+_options.TimelineOffsetMs,breathing.Start,breathing.End,breathing.Start>0,_options.ReduceMotion);
        }
        if(animationTime is { } dotsClock)_dots.AdvancePlayback(position,_song.Untimed?null:gap,dotsClock,_clock.IsPlaying,_clock.Generation,_song.Id,_options,now>=_manualUntil,_pageKaraoke);
        else if(gap is null||_song.Untimed)_dots.Visibility=Visibility.Collapsed;
        bool breathingNow=gap is not null&&!_song.Untimed&&(animationTime is null||_dots.PlaybackActive);
        bool breathingChanged=_breathing!=breathingNow;_breathing=breathingNow;
        if(now>=_manualUntil&&(anchor!=_anchor||breathingChanged||slotChange||_manualUntil>0))
        {_anchor=anchor;_manualUntil=0;Follow(anchor,breathingNow);}
        if(_autoScrolling)
        {
            double p=(now-_scrollStart)/_scrollDuration;
            if(p>=1){_scroll.ScrollToVerticalOffset(_scrollTo);_autoScrolling=false;}
            else _scroll.ScrollToVerticalOffset(_scrollFrom+(_scrollTo-_scrollFrom)*ScrollEase(p,_scrollTransition));
        }
        bool sourceLegacy=_options.Background is BackgroundMode.Static or BackgroundMode.Legacy or BackgroundMode.DynamicBeating||_options.ReduceMotion;
        _reference.SetLegacyVisible(sourceLegacy);
        if(sourceLegacy){if(!_options.ReduceMotion&&IsVisible&&WindowState!=WindowState.Minimized)_legacyTime+=dt;int rate=Math.Clamp(_options.AppleMusicLyrics.BackgroundDynamicFrameRate,1,120);_reference.UpdateLegacy(Math.Floor(_legacyTime*rate)/rate,_options.LightTheme,_options.Background==BackgroundMode.Static||_options.ReduceMotion);}
        _background.Update(_clock.IsPlaying,_portrait,IsVisible&&WindowState!=WindowState.Minimized,_options,dt);
        UpdateIndependentPresentation(_background.IndependentRendererActive&&_background.RendererReady);
        _background.Visibility=sourceLegacy?Visibility.Hidden:Visibility.Visible;
        UpdateArtworkScale();
        UpdateArtworkVideo();
        _reference.Get<Border>("BorderFpsOverlay").Visibility=_options.ShowFps?Visibility.Visible:Visibility.Collapsed;
        if(now-_lastFps>=_options.AppleMusicLyrics.FpsOverlayUpdateInterval){_fps.Text=$"{(_frameCount-_lastFpsFrames)/Math.Max(now-_lastFps,.001):0} FPS";_lastFps=now;_lastFpsFrames=_frameCount;}
    }
    private void RebuildRows()
    {
        if(!_args.Contains("--verify-offscreen"))_dots.RestartForLayout();
        double pageWidth=_reference.View.ActualWidth,pageHeight=_reference.View.ActualHeight;
        _portrait=pageHeight/Math.Max(1,pageWidth)>.85;
        bool compact=_portrait;
        bool empty=_song.Lines.Count==0;
        _leftColumn.Width=compact&&!empty?new GridLength(0):new GridLength(.82,GridUnitType.Star);
        _reference.Get<ColumnDefinition>("ColumnDefinitionRightSide").Width=empty?new GridLength(0):new GridLength(1,GridUnitType.Star);
        _lyricHost.Visibility=empty?Visibility.Hidden:Visibility.Visible;
        _infoView.Visibility=compact&&!empty?Visibility.Collapsed:Visibility.Visible;_smallInfo.Visibility=compact&&!empty?Visibility.Visible:Visibility.Hidden;
        _stage.Margin=_portrait?new Thickness(-6,0,-6,0):new Thickness(0);
        _infoView.Margin=new Thickness(0,_portrait?50:75,0,_portrait?50:75);
        _reference.Get<StackPanel>("StackPanelSongInfo").Margin=_portrait?new Thickness(-20,0,-20,0):new Thickness(0);
        _verticalControls.MaxHeight=pageHeight/3;
        if(!compact||empty)_verticalControlsShown=false;
        _verticalControls.Visibility=compact&&!empty&&_verticalControlsShown?Visibility.Visible:Visibility.Hidden;
        Grid.SetColumnSpan(_reference.Get<Grid>("GridMagicButton"),compact?2:1);
        _reference.Get<Grid>("GridMagicButton").Height=compact?90:120;
        _magic.Visibility=_options.MagicStrip?Visibility.Visible:Visibility.Collapsed;
        foreach(var drag in _buttonDrags)drag.Refresh();
        _reference.SetMotion(!_options.ReduceMotion,_options.AppleMusicLyrics.EnableTrackInfoMarquee);
        _font=ReferencePageView.FontFor(_options.AppleMusicLyrics.FontPreset,_options.AppleMusicLyrics.FontFamily);
        _reference.View.FontFamily=_font;
        _seek.FontFamily=_options.AppleMusicLyrics.FontPreset==FontPresets.SFPro?ReferencePageView.AppleTextFont():_font;_seekVertical.FontFamily=_font;
        _reference.ApplyMobileInfoStyle(_options.AppleMusicLyrics.IosStylePreset);
        foreach(var menu in _reference.Elements.OfType<ContextMenu>())menu.FontFamily=_font;
        UpdateZoomContext(compact,_fullscreen||WindowState==WindowState.Maximized,pageHeight);
        double scale=1/_inverseZoom;
        _displayScale=scale;
        var lyricsView=_reference.Get<Grid>("ViewBoxLyrics");lyricsView.Margin=compact?new Thickness(0,-95+185*scale,0,0):new Thickness(0,0,30,0);
        double width=Math.Max(100,_lyricHost.ActualWidth-lyricsView.Margin.Right);
        _scroll.Width=width;_scroll.Height=pageHeight;
        var scrollGrid=_reference.Get<Grid>("GridLyricsInViewbox");scrollGrid.Width=width;scrollGrid.Height=pageHeight;
        _reference.Get<Grid>("GridSongInfoSmall").Width=width/scale;
        _reference.Get<Grid>("GridSongInfoSmallOverlay").Width=width/scale;
        var verticalBox=_reference.Get<Viewbox>("ViewboxVerticalControls");verticalBox.Margin=new Thickness(20*scale*1.37,verticalBox.Margin.Top,20*scale*1.37,verticalBox.Margin.Bottom);
        _linesPanel.Margin=new Thickness(8.2*scale,450*scale,8.2*scale,0);
        ((SpacedStackPanel)_linesPanel).Spacing=4*scale;
        double infoHeight=_reference.Get<StackPanel>("StackPanelSongInfo").ActualHeight;
        double albumCenter=(_stage.ActualHeight-_infoView.ActualHeight)/2+_infoView.ActualHeight*165/Math.Max(1,infoHeight);
        _dots.Width=60*scale;_dots.Margin=new Thickness(24*scale,compact?130-10*scale:albumCenter-25*scale,24*scale,0);
        var previousRows=_resetWordRuns?[]:_rows.ToArray();_resetWordRuns=false;
        _linesPanel.Children.Clear();_rows.Clear();_credit=null;
        var groups=LyricsPreparation.ForPage(_song,_options.Duet,_options.RightAlignFirst,_options.AppleMusicLyrics,_options.EnableMultiLine);_groups=groups;
        bool karaoke=_pageKaraoke=LyricsPreparation.UseKaraoke(_song,_options.AppleMusicLyrics);
        int rowIndex=0;
        foreach(var data in groups)
        {
            var host=new StackPanel();
            bool duetInset=_options.Duet&&_song.Lines.Any(x=>x.AlignLeft==false);
            var main=new LyricRow(data.Main,data.Right,false,width-16.4*scale,scale,_font,_options,duetInset,portrait:_portrait,karaoke:karaoke,haveRomanization:_song.Lines.Any(l=>l.Romanization.Length>0));main.SeekRequested+=value=>Seek(value);
            main.HoverAllowed=()=>_pointerAtScroll is not { } point||Mouse.GetPosition(_reference.View)!=point;main.AnimationClockNow=()=>_wall.Elapsed.TotalMilliseconds;
            main.OpacityTransition=LineMotion.PageTransition(groups,rowIndex,karaoke,_options.AppleMusicLyrics);
            main.NextLineStartMs=karaoke?WordSweep.NextLineStart(groups,rowIndex):-1;
            RowSlot? previousRow=rowIndex<previousRows.Length?previousRows[rowIndex]:null;rowIndex++;
            bool overlap=_options.AppleMusicLyrics.BuggyApple&&_options.AppleMusicLyrics.BuggyAppleOptions.BackgroundVocalsOverlap&&(rowIndex==1?data.StartMs>6500:data.StartMs-groups[rowIndex-2].EndMs>6000);
            if(previousRow is not null)main.RestoreWordRun(previousRow.Main);
            main.SingOpacityDuration=_singOpacityDuration;
            main.HaveAnyTranslation=_song.Lines.Any(l=>!string.IsNullOrWhiteSpace(l.Translation));main.RefreshCopyMenu();
            main.CopyRequested+=(line,kind)=>Guard(()=>{Clipboard.SetText(CopyText.Build(kind,line,_song.Lines,_groups.Select(g=>g.Main)));Notify("已复制");});
            var backingRows=new List<LyricRow>();
            host.Children.Add(main);
            foreach(var backing in data.Backgrounds.Where(b=>b.StartMs<data.Main.StartMs))AddBacking(backing);
            foreach(var backing in data.Backgrounds.Where(b=>b.StartMs>=data.Main.StartMs))AddBacking(backing);
            if(previousRow is not null)for(int i=0;i<Math.Min(backingRows.Count,previousRow.Backgrounds.Count);i++)backingRows[i].RestoreWordRun(previousRow.Backgrounds[i]);
            _linesPanel.Children.Add(host);var slot=new RowSlot(data,host,main,backingRows);host.RenderTransform=slot.Pull;_rows.Add(slot);
            void AddBacking(LyricLine line){bool above=_options.AppleMusicLyrics.BackgroundVocalsNativePosition&&line.StartMs<data.Main.StartMs;var row=new LyricRow(line,data.Right,true,main.TextWidth,scale,_font,_options,duetInset,above,_portrait,karaoke,haveRomanization:_song.Lines.Any(l=>l.Romanization.Length>0)){BackgroundOverlap=overlap,OpacityTransition=main.OpacityTransition,BackgroundExitTransition=rowIndex<groups.Count?LineMotion.PageTransition(groups,rowIndex,karaoke,_options.AppleMusicLyrics):(false,.2),SingOpacityDuration=_singOpacityDuration};main.AddBackground(row,above);backingRows.Add(row);}
        }
        if(groups.Count==0)_linesPanel.Children.Add(new TextBlock{Text="暂无歌词\n导入 LRC、TTML 或 JSON 歌词",FontSize=25,Foreground=Brushes.White,Opacity=.5,Margin=new Thickness(30)});
        if(!string.IsNullOrWhiteSpace(_song.Credit))
        {
            _credit=(UserControl)Application.LoadComponent(new Uri("/LyricReplica;component/Assets/ReferenceCredit.xaml",UriKind.Relative));_credit.FontFamily=_font;_credit.FontSize=(compact?28:24)*scale;
            ((StackPanel)_credit.FindName("StackPanelLyrics")).Margin=new Thickness(20*scale,15*scale,20*scale,15*scale);
            ((System.Windows.Documents.Run)_credit.FindName("RunWrittenBy")).Text="";
            var writers=(System.Windows.Documents.Run)_credit.FindName("RunWriters");writers.Text=_song.Credit;writers.FontWeight=_options.Bold?FontWeights.Bold:FontWeights.Medium;
            ((TextBlock)_credit.FindName("TextBlockLyrics")).LineHeight=30*scale;
            _ = new RowFeedback(_credit,(Grid)_credit.FindName("GridInside"),(Border)_credit.FindName("BorderBackground"),()=>!_options.ReduceMotion,()=>_pointerAtScroll is not { } point||Mouse.GetPosition(_reference.View)!=point);
            _credit.MouseLeftButtonUp+=(_,_)=>Seek(Math.Max(0,_song.DurationMs-1000));_linesPanel.Children.Add(_credit);
        }
        _linesPanel.Children.Add(new Border{Height=Math.Max(250,_scroll.ActualHeight*.65)});
        // The compact information view must have its new height before its fade is measured.
        _root.UpdateLayout();
        _scroll.OpacityMask=null;
        var maskHost=_reference.Get<Grid>("GridLyricsMaskHost");
        if(compact)
        {
            double h=_smallInfo.ActualHeight,gridHeight=_reference.Get<Grid>("GridSongInfoSmall").ActualHeight;
            double top=15+h/(gridHeight+70)*(gridHeight+35);
            var mask=new LinearGradientBrush{StartPoint=new Point(.5,0),EndPoint=new Point(.5,1)};
            mask.GradientStops.Add(new GradientStop(Colors.Transparent,top/Math.Max(1,pageHeight)));mask.GradientStops.Add(new GradientStop(Colors.White,(top+65*scale)/Math.Max(1,pageHeight)));maskHost.OpacityMask=mask;
            mask.GradientStops.Add(new GradientStop(Colors.White,_verticalControlsShown?1-(_verticalControls.ActualHeight+100)/Math.Max(1,pageHeight):1));
            mask.GradientStops.Add(new GradientStop(Colors.Transparent,_verticalControlsShown?1-(_verticalControls.ActualHeight-5)/Math.Max(1,pageHeight):1));
        }
        else maskHost.OpacityMask=null;
        bool sourceBackground=_options.Background is BackgroundMode.Static or BackgroundMode.Legacy or BackgroundMode.DynamicBeating||_options.ReduceMotion;
        bool independent=_options.Background==BackgroundMode.Modern&&_options.AppleMusicLyrics.EnableIndependentBackgroundRenderer&&WindowState==WindowState.Maximized;
        _blend.Update(_options.AppleMusicLyrics.EnablePlusLighter&&!independent,_options.AppleMusicLyrics.PlusLighterEffectPreset,sourceBackground?_reference.Get<Viewbox>("ViewboxBackground"):_background,!sourceBackground);
        // Follow reads the new row offsets in this same rendering callback.
        _scroll.UpdateLayout();
        _anchor=-2;_autoScrolling=false;
    }
    private void SetVerticalControls(bool shown)
    {
        _verticalControlsShown=shown&&_portrait;
        _verticalControls.Visibility=_verticalControlsShown?Visibility.Visible:Visibility.Hidden;
        if(_reference.Get<Grid>("GridLyricsMaskHost").OpacityMask is LinearGradientBrush mask&&mask.GradientStops.Count==4)
        {
            double height=Math.Max(1,_lyricHost.ActualHeight);
            mask.GradientStops[2].Offset=_verticalControlsShown?1-(_verticalControls.ActualHeight+100)/height:1;
            mask.GradientStops[3].Offset=_verticalControlsShown?1-(_verticalControls.ActualHeight-5)/height:1;
        }
    }
    private void Follow(int index,bool breathing=false)
    {
        if(_song.Untimed)return;
        _pointerAtScroll=Mouse.GetPosition(_reference.View);
        _scrollFrom=_scroll.VerticalOffset;
        if(index<0&&(!breathing||_rows.Count==0))_scrollTo=0;
        else if(index>=_rows.Count)
        {
            if(_credit is null){_autoScrolling=false;return;}
            double y=VisualTreeHelper.GetOffset(_credit).Y;
            if(_portrait)_scrollTo=Math.Max(0,y+475*_displayScale-130);
            else
            {
                double infoHeight=_reference.Get<StackPanel>("StackPanelSongInfo").ActualHeight;
                double albumCenter=(_stage.ActualHeight-_infoView.ActualHeight)/2+_infoView.ActualHeight*165/Math.Max(1,infoHeight);
                _scrollTo=Math.Max(0,y+450*_displayScale-albumCenter+(((StackPanel)_credit.FindName("StackPanelLyrics")).ActualHeight+30*_displayScale)/2);
            }
        }
        else
        {
            int target=Math.Max(0,index);double occupied=0;
            if(breathing&&index>=0&&index+1<_rows.Count)
            {
                target=index+1;occupied=BackgroundSpace(_rows[index]);
                if(index>0&&_rows[index-1].Data.EndMs==_rows[index].Data.EndMs)occupied+=BackgroundSpace(_rows[index-1]);
            }
            var row=_rows[target];double y=VisualTreeHelper.GetOffset(row.Host).Y-occupied;
            if(!breathing&&index>0&&(_groups[index].StartMs-_groups[index-1].EndMs<=6000))
            {y-=BackgroundSpace(_rows[index-1]);if(index>1&&_groups[index-2].EndMs==_groups[index-1].EndMs)y-=BackgroundSpace(_rows[index-2]);}
            if(_portrait)_scrollTo=Math.Max(0,y+475*_displayScale-130-(breathing?83*_displayScale:0));
            else
            {
                double infoHeight=_reference.Get<StackPanel>("StackPanelSongInfo").ActualHeight;
                double albumCenter=(_stage.ActualHeight-_infoView.ActualHeight)/2+_infoView.ActualHeight*165/Math.Max(1,infoHeight);
                _scrollTo=Math.Max(0,y+450*_displayScale-albumCenter+(breathing?-43.5*_displayScale:row.Host.ActualHeight/2));
            }
        }
        var transition=index>=0&&index<_rows.Count?_rows[index].Main.OpacityTransition:(false,.2);
        if(index<0&&breathing){_autoScrolling=false;_scroll.ScrollToVerticalOffset(_scrollTo);return;}
        if(!_options.ReduceMotion&&index>=0&&index<_rows.Count&&(breathing||_options.AppleMusicLyrics.PullingEffect))
        {ApplyPulling(_scrollTo,index,breathing,transition);return;}
        _scrollStart=_wall.Elapsed.TotalSeconds;_scrollDuration=_options.ReduceMotion?.001:Motion.ScrollDurationMs(_options.AppleMusicLyrics,_pageKaraoke)/1000d*_options.AnimationSpeed;_scrollTransition=transition;_autoScrolling=true;
        static double BackgroundSpace(RowSlot row)=>row.Backgrounds.Sum(back=>back.ActualHeight+back.Margin.Top);
    }
    private void ManualScroll(){_pointerAtScroll=Mouse.GetPosition(_reference.View);_lastManualScroll=_wall.Elapsed.TotalSeconds;_manualUntil=_lastManualScroll+_options.AutoResumeSeconds;_autoScrolling=false;_dots.SuspendForManualScroll();for(int i=0;i<PullCount;i++)AnimatePull(i,0,0,Motion.ScrollDurationMs(_options.AppleMusicLyrics,_pageKaraoke)*_options.AnimationSpeed,0,(false,.2));}
    private void ExecuteMagic(MagicAction action)
    {
        switch(action)
        {
            case MagicAction.Close:Close();break;
            case MagicAction.Maximize:WindowState=WindowState.Maximized;_rebuildPending=true;break;
            case MagicAction.Restore:_fullscreen=false;WindowState=WindowState.Normal;_rebuildPending=true;break;
            case MagicAction.Minimize:WindowState=WindowState.Minimized;break;
            case MagicAction.Top:ManualScroll();Follow(0);break;
            case MagicAction.Bottom:ManualScroll();Follow(_rows.Count-1);break;
            case MagicAction.Current:_manualUntil=0;_anchor=-2;Follow(Timeline.Anchor(_groups,_clock.PositionMs+_options.TimelineOffsetMs),_breathing);break;
        }
    }
    public async void Seek(double ms)
    {
        try
        {
            if(_system is { } system){await system.SeekAsync(Math.Clamp(ms,0,_clock.DurationMs));return;}
            _clock.Seek(ms);_manualUntil=0;_anchor=-2;_autoScrolling=false;
            if(_audioLoaded)_audio.Position=TimeSpan.FromMilliseconds(_clock.PositionMs);
        }
        catch(Exception e){Notify(e.Message);App.Log(e.ToString());}
    }
    public async void TogglePlay()
    {
        if(_navigationRequest!=0){_navigationPlaying=!_navigationPlaying;UpdatePlayButtons();return;}
        try
        {
            if(_system is { } system){if(_clock.IsPlaying)await system.PauseAsync();else await system.PlayAsync();return;}
            if(_clock.IsPlaying){_clock.Pause();_audio.Pause();}
            else{if(_clock.PositionMs>=_clock.DurationMs)_clock.Seek(0);_clock.Play();if(_audioLoaded)_audio.Play();}
            UpdatePlayButtons();
        }
        catch(Exception e){Notify(e.Message);App.Log(e.ToString());}
    }
    private void UpdatePlayButtons()
    {
        bool playing=_navigationRequest!=0?_navigationPlaying:_clock.IsPlaying;
        _reference.Get<ImageButton>("ImageClickablePauseButton").Visibility=playing?Visibility.Visible:Visibility.Collapsed;_reference.Get<ImageButton>("ImageClickablePlayButton").Visibility=playing?Visibility.Collapsed:Visibility.Visible;
        var shuffle=_reference.Get<ImageButton>("ImageClickableShuffleButton");var repeat=_reference.Get<ImageButton>("ImageClickableRepeatButton");
        shuffle.IconOpacity=_shuffle?1:.3;repeat.IconOpacity=_repeat?1:.3;
        shuffle.IsEnabled=_system is null||_system.Connected&&_remoteShuffle;repeat.IsEnabled=_system is null||_system.Connected&&_remoteRepeat;
        ToolTipService.SetShowOnDisabled(shuffle,true);ToolTipService.SetShowOnDisabled(repeat,true);
        shuffle.ToolTip=shuffle.IsEnabled?"随机播放":"此媒体会话未提供随机播放控制";repeat.ToolTip=repeat.IsEnabled?"循环播放":"此媒体会话未提供循环控制";
        bool one=_system is null?_repeat:_remoteRepeatMode==MediaPlaybackAutoRepeatMode.Track;
        repeat.ImageSource=(ImageSource)Application.Current.Resources[one?"Apple.Music.RepeatOneButton":"Apple.Music.RepeatButton"];
    }
    private async void ToggleShuffle()
    {
        try{if(_system is { } system){await system.ShuffleAsync(!_shuffle);return;}_shuffle=!_shuffle;UpdatePlayButtons();}
        catch(Exception e){Notify(e.Message);App.Log(e.ToString());}
    }
    private async void ToggleRepeat()
    {
        try
        {
            if(_system is { } system){var mode=_remoteRepeatMode switch{MediaPlaybackAutoRepeatMode.None=>MediaPlaybackAutoRepeatMode.List,MediaPlaybackAutoRepeatMode.List=>MediaPlaybackAutoRepeatMode.Track,_=>MediaPlaybackAutoRepeatMode.None};await system.RepeatAsync(mode);return;}
            _repeat=!_repeat;UpdatePlayButtons();
        }
        catch(Exception e){Notify(e.Message);App.Log(e.ToString());}
    }
    private void UpdateArtworkScale()
    {
        bool playing=_clock.IsPlaying;double target=playing?1:.75;
        if(_options.ReduceMotion){_artworkScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);_artworkScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);_artworkScale.ScaleX=_artworkScale.ScaleY=target;_coverPlaying=playing;return;}
        if(_coverPlaying==playing)return;
        IEasingFunction easing=playing?new BackEase{Amplitude=.7,EasingMode=EasingMode.EaseOut}:new CubicEase{EasingMode=EasingMode.EaseOut};
        var animation=new DoubleAnimation(target,TimeSpan.FromMilliseconds(400)){EasingFunction=easing,BeginTime=TimeSpan.FromMilliseconds(_coverPlaying is null&&playing?300:0)};
        _artworkScale.BeginAnimation(ScaleTransform.ScaleXProperty,animation);_artworkScale.BeginAnimation(ScaleTransform.ScaleYProperty,animation);_coverPlaying=playing;
    }
    private async void Next(){try{if(_system is { } system){await system.NextAsync();return;}await NavigateLocalAsync(_shuffle?Random.Shared.Next(_songs.Count):_songIndex+1);}catch(Exception e){Notify(e.Message);App.Log(e.ToString());}}
    private async void Previous(){try{if(_system is { } system){await system.PreviousAsync();return;}if(_clock.PositionMs>3000){Seek(0);return;}await NavigateLocalAsync(_songIndex-1);}catch(Exception e){Notify(e.Message);App.Log(e.ToString());}}
    private async Task NavigateLocalAsync(int index)
    {
        bool playing=_navigationRequest!=0?_navigationPlaying:_clock.IsPlaying;
        int generation=_loadGeneration+1;_navigationRequest=generation;_navigationPlaying=playing;
        try
        {
            await LoadSongAsync(index);
            if(_disposed||generation!=_loadGeneration)return;
            if(_navigationPlaying){_clock.Play();if(_audioLoaded)_audio.Play();}
        }
        finally{if(_navigationRequest==generation){_navigationRequest=0;_navigationPlaying=false;}if(!_disposed)UpdatePlayButtons();}
    }
    private void EndTrack(){if(!_clock.IsPlaying||_system is not null)return;if(_repeat){Seek(0);if(_audioLoaded)_audio.Play();}else Next();}
    private void Favorite(){if(!_favorites.Add(_song.Id))_favorites.Remove(_song.Id);IsHearted=_favorites.Contains(_song.Id);Notify(IsHearted?"已加入收藏":"已取消收藏");RefreshQueue();}
    private void ToggleQueue(){_queue.Visibility=_queue.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;_settings.Visibility=Visibility.Collapsed;RefreshQueue();}
    private void RefreshQueue()
    {
        _queueItems.Children.Clear();
        for(int i=0;i<_songs.Count;i++){int index=i;var s=_songs[i];var item=Button(($"{(_favorites.Contains(s.Id)?"♥  ":"")}{s.Title}\n{s.Artist}"),$"选择歌曲 {s.Title}",()=>_ = LoadSongAsync(index),230,62);item.HorizontalContentAlignment=HorizontalAlignment.Left;if(index==_songIndex)item.Background=new SolidColorBrush(Color.FromArgb(40,255,255,255));_queueItems.Children.Add(item);}
    }
    private void Notify(string text){_status.Text=text;_noticeUntil=_wall.Elapsed.TotalSeconds+3;}
    private void Guard(Action action){try{action();}catch(Exception e){Notify(e.Message);App.Log(e.ToString());}}
    private void OpenLyrics(){var dialog=new OpenFileDialog{Filter="歌词|*.json;*.lrc;*.ttml;*.xml;*.txt|所有文件|*.*"};if(ShowFileDialog(dialog))ImportLyrics(dialog.FileName);}
    public void ImportLyrics(string path){Guard(()=>{var song=LyricsFile.Load(path);_songs.Add(song);_ = LoadSongAsync(_songs.Count-1);});}
    private void OpenAudio(){var dialog=new OpenFileDialog{Filter="音频|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac|所有文件|*.*"};if(ShowFileDialog(dialog))OpenAudioFile(dialog.FileName);}
    private void OpenAudioFile(string path){_system?.Dispose();_system=null;_clock.Pause();_clock.Seek(0);UpdatePlayButtons();_song.AudioPath=path;_audio.Open(new Uri(Path.GetFullPath(path)));_audioLoaded=true;_audio.Volume=_options.Volume;Notify(Path.GetFileName(path));}
    private void OpenCover(){var dialog=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp"};if(ShowFileDialog(dialog)){var cover=Artwork.Load(dialog.FileName);_song.CoverPath=dialog.FileName;_hasExplicitArtwork=true;_artwork.Source=_smallArtwork.Source=cover;_ = _background.SetArtworkAsync(cover,_song.Id+dialog.FileName);}}
    private bool ShowFileDialog(OpenFileDialog dialog)
    {
        if(_fileDialogOpen||_wall.Elapsed.TotalSeconds<_ignoreDialogMouseUntil)return false;
        _fileDialogOpen=true;
        try{return dialog.ShowDialog(this)==true;}
        finally{_fileDialogOpen=false;_ignoreDialogMouseUntil=_wall.Elapsed.TotalSeconds+.25;}
    }
    private void OnDrop(object sender,DragEventArgs e){if(e.Data.GetData(DataFormats.FileDrop) is string[] paths&&paths.Length>0)ImportLyrics(paths[0]);}
    private async void ConnectSystemMedia()
    {
        var connection=new SystemMediaConnection(Dispatcher);
        try
        {
            _audio.Stop();_audioLoaded=false;_system?.Dispose();_system=connection;
            _remoteShuffle=_remoteRepeat=_shuffle=_repeat=false;_remoteRepeatMode=null;UpdatePlayButtons();
            connection.Changed+=snapshot=>ApplySystemMediaSnapshot(connection,snapshot);
            connection.Disconnected+=()=>{if(!_disposed&&ReferenceEquals(_system,connection)){_clock.Pause();_dots.StopPlayback();UpdatePlayButtons();Notify("等待系统媒体会话恢复");}};
            connection.Error+=message=>{if(!_disposed&&ReferenceEquals(_system,connection))Notify(message);};await connection.ConnectAsync();
        }
        catch(Exception e){connection.Dispose();if(ReferenceEquals(_system,connection)){_system=null;if(!_disposed)Notify(e.Message);}App.Log(e.ToString());}
    }
    private void ApplySystemMediaSnapshot(SystemMediaConnection connection,MediaSnapshot snapshot)
    {
        if(_disposed||!ReferenceEquals(_system,connection))return;
        if(!string.Equals(snapshot.Title.Trim(),_song.Title.Trim(),StringComparison.OrdinalIgnoreCase))
        {_clock.Pause();_dots.StopPlayback();UpdatePlayButtons();Notify("播放器曲目与歌词不匹配，请导入对应歌词。");return;}
        if(snapshot.DurationMs is >0 and <=86_400_000&&Math.Abs(snapshot.DurationMs-_clock.DurationMs)>.5){double p=_clock.PositionMs;_clock.Load(snapshot.DurationMs);_clock.Seek(p);}
        if(Math.Abs(snapshot.PositionMs-_clock.PositionMs)>120)_clock.Seek(snapshot.PositionMs);
        _shuffle=snapshot.ShuffleActive==true;_remoteRepeatMode=snapshot.RepeatMode;_repeat=snapshot.RepeatMode is { } mode&&mode!=MediaPlaybackAutoRepeatMode.None;_remoteShuffle=snapshot.CanShuffle;_remoteRepeat=snapshot.CanRepeat;
        if(snapshot.Playing)_clock.Play();else _clock.Pause();
        _title.Text=_smallTitle.Text=_song.Title;_artist.Text=_smallArtist.Text=string.IsNullOrWhiteSpace(_song.Artist)?snapshot.Artist:_song.Artist;UpdatePlayButtons();
        if(snapshot.Artwork is { } artwork&&!ReferenceEquals(_mediaArtwork,artwork))_ = UpdateMediaArtworkAsync(connection,artwork);
    }
    private async Task UpdateMediaArtworkAsync(SystemMediaConnection connection,byte[] bytes)
    {
        if(_disposed||!ReferenceEquals(_system,connection)||ReferenceEquals(_mediaArtwork,bytes))return;
        _mediaArtwork=bytes;
        if(_hasExplicitArtwork)return;
        try
        {
            var cover=Artwork.Decode(bytes);_artwork.Source=_smallArtwork.Source=cover;
            await _background.SetArtworkAsync(cover,_song.Id+":"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        }
        catch(Exception e){if(!_disposed&&ReferenceEquals(_system,connection))Notify("媒体封面无法读取："+e.Message);App.Log(e.ToString());}
    }
    private void ToggleFullscreen()
    {
        if(!_fullscreen){_restoreBounds=new Rect(Left,Top,Width,Height);_restoreState=WindowState;WindowState=WindowState.Normal;_fullscreen=true;WindowState=WindowState.Maximized;}
        else{_fullscreen=false;WindowState=WindowState.Normal;Left=_restoreBounds.Left;Top=_restoreBounds.Top;Width=_restoreBounds.Width;Height=_restoreBounds.Height;WindowState=_restoreState;}
        _rebuildPending=true;
    }
    private void OnKey(object sender,KeyEventArgs e)
    {
        if(HandleKey(e.Key,Keyboard.Modifiers,e.OriginalSource is TextBox))e.Handled=true;
    }
    private bool HandleKey(Key key,ModifierKeys modifiers,bool textInput=false)
    {
        if(textInput)return false;
        bool ctrl=(modifiers&ModifierKeys.Control)!=0,alt=(modifiers&ModifierKeys.Alt)!=0;
        if(key==Key.Space)TogglePlay();
        else if(key==Key.F11)ToggleFullscreen();
        else if(ctrl&&key==Key.T){_options.Topmost=!_options.Topmost;Topmost=_options.Topmost;}
        else if(ctrl&&key is Key.Add or Key.OemPlus)ChangeZoom(true);
        else if(ctrl&&key is Key.Subtract or Key.OemMinus)ChangeZoom(false);
        else if(ctrl&&key==Key.D0)ResetZoom();
        else if(alt&&key==Key.C){_options.Translation=!_options.Translation;_rebuildPending=true;}
        else if(alt&&key==Key.R){_options.Romanization=!_options.Romanization;_rebuildPending=true;}
        else if(key==Key.Escape){_settings.Visibility=_queue.Visibility=Visibility.Collapsed;if(_fullscreen)ToggleFullscreen();}
        else return false;
        return true;
    }
    private void ChangeZoom(bool zoomIn)
    {
        SetZoom(ZoomRules.AdjustInverse(_inverseZoom,zoomIn));
    }
    private void ResetZoom()
    {
        bool fullscreen=_fullscreen||WindowState==WindowState.Maximized;
        double height=_reference.View.ActualHeight;bool tall=height/Math.Max(1,_reference.View.ActualWidth)>1;
        SetZoom(ZoomRules.DefaultInverse(tall,fullscreen,height));
    }
    private void UpdateZoomContext(bool portrait,bool maximized,double height)
    {
        var settings=_options.AppleMusicLyrics;var levels=settings.ZoomingLevels;
        int context=(portrait?1:0)+(maximized?2:0);
        double saved=ZoomRules.Remembered(levels,portrait,maximized);
        if(context!=_zoomContext||_zoomSettings!=settings||_zoomLevels!=levels||_zoomRestore!=settings.ZoomingRestore||!saved.Equals(_rememberedZoom))
        {
            bool legacy=_zoomContext<0&&settings.ZoomingRestore&&_options.Zoom!=1&&new[]{levels.Horizontal,levels.Vertical,levels.HorizontalFullscreen,levels.VerticalFullscreen}.All(double.IsNaN);
            _inverseZoom=ZoomRules.Restore(settings,portrait,maximized,height);
            if(legacy){_inverseZoom/=_options.Zoom;ZoomRules.Remember(settings,portrait,maximized,_inverseZoom);}
            _zoomContext=context;_zoomSettings=settings;_zoomLevels=levels;_zoomRestore=settings.ZoomingRestore;
            _rememberedZoom=ZoomRules.Remembered(levels,portrait,maximized);
        }
        _options.Zoom=ZoomRules.DefaultInverse(portrait,maximized,height)/_inverseZoom;
    }
    private void SetZoom(double inverse)
    {
        _inverseZoom=inverse;
        bool maximized=_fullscreen||WindowState==WindowState.Maximized;
        ZoomRules.Remember(_options.AppleMusicLyrics,_portrait,maximized,inverse);
        _rememberedZoom=ZoomRules.Remembered(_options.AppleMusicLyrics.ZoomingLevels,_portrait,maximized);
        _options.Zoom=ZoomRules.DefaultInverse(_portrait,maximized,_reference.View.ActualHeight)/inverse;
        App.SaveOptions(_options);_rebuildPending=true;
    }
    private ContextMenu BuildContextMenu()
    {
        var menu=_reference.Get<ContextMenu>("MenuRightClick");ReferenceMenus.Apply(menu);
        Action[] commands=[()=>{_options.Topmost=!_options.Topmost;Topmost=_options.Topmost;},()=>ChangeZoom(true),()=>ChangeZoom(false),ResetZoom,()=>{_options.Blur=!_options.Blur;_rebuildPending=true;},()=>{_options.Bold=!_options.Bold;_rebuildPending=true;},()=>{_options.Translation=!_options.Translation;_rebuildPending=true;}];
        var originalItems=menu.Items.OfType<MenuItem>().ToArray();
        if(originalItems.Length!=commands.Length)throw new InvalidOperationException("Reference context menu changed.");
        for(int i=0;i<commands.Length;i++){int command=i;originalItems[i].Click+=(_,_)=>Guard(()=>{commands[command]();RefreshContextMenu();});}
        menu.Opened+=(_,_)=>RefreshContextMenu();RefreshContextMenu();
        menu.Items.Add(new Separator());Add("导入歌词…",OpenLyrics);Add("打开本地音频…",OpenAudio);Add("连接系统媒体",ConnectSystemMedia);
        menu.Items.Add(new Separator());Add("设置",ToggleSettings);Add("播放列表",ToggleQueue);Add("全屏  F11",ToggleFullscreen);Add("返回当前歌词",()=>{_manualUntil=0;_anchor=-2;});Add("关闭",Close);
        return menu;
        void Add(string title,Action action){var item=new MenuItem{Header=title};item.Click+=(_,_)=>Guard(action);menu.Items.Add(item);}
    }
    private void RefreshContextMenu()
    {
        foreach(var (name,enabled) in new[]{("MenuItemTopmost",_options.Topmost),("MenuItemBlur",_options.Blur),("MenuItemBoldText",_options.Bold),("MenuItemTranslation",_options.Translation)})_reference.Get<IconMenuItem>(name).SymbolIcon=enabled?"Checkmark12":"Empty";
    }
    private void ConnectTrackMenus()
    {
        var menu=_reference.Get<ContextMenu>("MenuThreeDot");ReferenceMenus.Apply(menu);
        _reference.Get<MenuItem>("MenuItemAddToLibrary").Click+=(_,_)=>Favorite();_reference.Get<MenuItem>("MenuItemDeleteFromLibrary").Click+=(_,_)=>Favorite();
        _reference.Get<MenuItem>("MenuItemOpenTrackManagement").Click+=(_,_)=>ToggleQueue();
        foreach(var item in menu.Items.OfType<MenuItem>().Where(i=>i.Name is not ("MenuItemAddToLibrary" or "MenuItemDeleteFromLibrary" or "MenuItemOpenTrackManagement"))){item.IsEnabled=false;item.ToolTip="当前播放源未提供 Spotify 导航信息";}
        var artist=_reference.Get<ContextMenu>("MenuArtistNameClick");ReferenceMenus.Apply(artist);
        foreach(var item in artist.Items.OfType<MenuItem>()){item.IsEnabled=false;item.ToolTip="当前播放源未提供 Spotify 导航信息";}
        foreach(var name in new[]{"TextBlockArtist","TextBlockVerticalModeArtistName"})_reference.Get<TextBlock>(name).MouseLeftButtonUp+=(sender,_)=>{RefreshTrackMenu();artist.PlacementTarget=(UIElement)sender;artist.IsOpen=true;};
    }
    private void RefreshTrackMenu()
    {
        _reference.Get<MenuItem>("MenuItemAddToLibrary").Visibility=IsHearted?Visibility.Collapsed:Visibility.Visible;
        _reference.Get<MenuItem>("MenuItemDeleteFromLibrary").Visibility=IsHearted?Visibility.Visible:Visibility.Collapsed;
        _reference.Get<TextBlock>("TextBlockGoToArtistContent").Text=_song.Artist;_reference.Get<TextBlock>("TextBlockGoToAlbumContent").Text=_song.Album;
    }
    private void ToggleSettings()
    {
        _settings.Visibility=_settings.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;_queue.Visibility=Visibility.Collapsed;
        if(_settings.Visibility!=Visibility.Visible){Guard(()=>App.SaveOptions(_options));return;}
        Populate();
        void Populate()=>_settings.Child=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new SettingsPanel(_options,Changed,()=>Guard(()=>{_options=new();Changed(true);Populate();Notify("已恢复默认设置");}),Populate)};
        void Changed(bool background)=>Guard(()=>
        {
            _options.Normalize();ReferenceMenus.UpdateTheme(_options.LightTheme);Topmost=_options.Topmost;_audio.Volume=_options.Volume;UpdateWindowChrome();
            UpdateSleepRequest();
            if(background){UpdateIndependentPresentation(false);_ = _background.SetModeAsync(_options.Background,_options,true);}
            App.SaveOptions(_options);_rebuildPending=true;_resetWordRuns=true;_anchor=-2;
        });
    }
    private void OnClosed(object? sender,EventArgs e)
    {
        if(_disposed)return;_disposed=true;CompositionTarget.Rendering-=OnRendering;_clock.Pause();_system?.Dispose();_audio.Close();StopArtworkVideo();_dots.Dispose();_magicController.Dispose();_blend.Dispose();_background.Dispose();
        ReleaseSleepRequest();
        try{App.SaveOptions(_options);}catch(Exception ex){App.Log(ex.ToString());}
    }
    private void UpdateSleepRequest()
    {if(!_sleepPrevention.Update(IsLoaded&&!_disposed,_options.AppleMusicLyrics.PreventSleepFullscreenOnly,WindowState))App.Log("Windows rejected the thread execution-state request.");}
    private void ReleaseSleepRequest()
    {if(!_sleepPrevention.Release())App.Log("Windows rejected restoring the thread execution state.");}
    public object State()=>new{title=_song.Title,positionMs=_clock.PositionMs,durationMs=_clock.DurationMs,playing=_clock.IsPlaying,generation=_clock.Generation,anchor=_anchor,portrait=_portrait,rows=_rows.Count,frames=_frameCount,background=_options.Background.ToString(),backgroundError=_background.LastError};
    private async Task RunSelfTestAsync()
    {
        File.Delete(Path.Combine(App.DataFolder,"self-test-failure.txt"));
        try
        {
            _options.Background=BackgroundMode.Legacy;_options.ReduceMotion=true;await _background.SetModeAsync(_options.Background,_options);
            await Task.Delay(200);_clock.Seek(16000);_rebuildPending=true;await Task.Delay(150);
            int before=_rows.Count;if(before==0)throw new Exception("No lyric rows");
            TogglePlay();await Task.Delay(100);TogglePlay();double paused=_clock.PositionMs;await Task.Delay(100);if(Math.Abs(_clock.PositionMs-paused)>1)throw new Exception("Pause drift");
            Seek(11000);await Task.Delay(50);if(Math.Abs(_clock.PositionMs-11000)>1)throw new Exception("Rewind failed");
            Seek(18000);Seek(10000);if(Math.Abs(_clock.PositionMs-10000)>1)throw new Exception("Consecutive seek failed");await Task.Delay(150);
            string folder=Path.Combine(App.DataFolder,"verification");Directory.CreateDirectory(folder);
            Capture(Path.Combine(folder,"landscape.png"));Width=700;Height=1000;await Task.Delay(200);if(!_portrait)throw new Exception("Portrait failed");Capture(Path.Combine(folder,"portrait.png"));
            await CheckNativeReflowAsync(folder);
            await CheckNativeWindowAsync(folder);
            await LoadSongAsync(1);await Task.Delay(150);if(!_rows.Any(r=>r.Backgrounds.Count>0))throw new Exception("Background vocals missing");
            await CheckNativeBackgroundsAsync(folder);
            int videoArgument=Array.IndexOf(_args,"--video-fixture");if(videoArgument>=0&&videoArgument+1<_args.Length)await CheckNativeArtworkVideoAsync(_args[videoArgument+1],folder);
            File.WriteAllText(Path.Combine(folder,"self-test.json"),JsonSerializer.Serialize(new{passed=true,assertions=8,state=State()},LyricsFile.JsonOptions));
            Application.Current.Shutdown(0);
        }
        catch(Exception e){App.Log(e.ToString());File.WriteAllText(Path.Combine(App.DataFolder,"self-test-failure.txt"),e.ToString());Application.Current.Shutdown(1);}
    }
    public void Capture(string path)
    {var bitmap=new RenderTargetBitmap(Math.Max(1,(int)ActualWidth),Math.Max(1,(int)ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(_root);File.WriteAllBytes(path,Artwork.Encode(bitmap));}

    public async Task VerifyOffscreenAsync(string folder)
    {
        if(!_args.Contains("--verify-offscreen"))throw new InvalidOperationException("Offscreen verification requires its isolated profile.");
        Content=null;
        Directory.CreateDirectory(folder);
        var checks=new List<string>();var layouts=new List<object>();
        try
        {
            SettingsPanelChecks.Run(Check);
            SleepPreventionChecks.Run(Check);
            InteractionFeedbackChecks.Run(Check);
            Check(!_sleepPrevention.IsHeld,"disconnected verification starts without a real system sleep request");
            ReferenceFontChecks.Run(Check);
            MagicStripChecks.Run(Check);
            await MagicStripChecks.RunDynamicAsync(Check);
            foreach(var style in new (int? Preset,double Artwork,double Column,double Artist,double ArtistTop,double TitleBottom)[]{(16,90,110,24.5,3,-1),(17,108,125,22,4,-3),(99,108,125,22,4,-3),(null,100,120,25,5,0)})
            {
                _options.AppleMusicLyrics.IosStylePreset=style.Preset;
                _reference.ApplyMobileInfoStyle(style.Preset);
                Check(_smallArtwork.Width==style.Artwork&&_smallArtwork.Height==style.Artwork&&_reference.Get<ColumnDefinition>("ViewboxSongInfoSmallColumnDefinition1").Width.Value==style.Column,"mobile information layout matches the recovered Loaded branch "+style.Preset);
                Check(_smallArtist.FontSize==style.Artist&&_smallArtist.Margin.Top==style.ArtistTop&&_reference.Get<Grid>("DockPanelVerticalModeTrackName").Margin.Bottom==style.TitleBottom,"mobile artist metrics and title spacing match the recovered Loaded branch "+style.Preset);
            }
            Check(_smallArtist.ReadLocalValue(TextBlock.FontFamilyProperty)==DependencyProperty.UnsetValue,"returning to default mobile style restores inherited font selection");
            using(var alignmentFixture=JsonDocument.Parse(typeof(MainWindow).Assembly.GetManifestResourceStream("layout-original.json")!))
            {
                Check(alignmentFixture.RootElement.GetProperty("originalStartCalled").GetBoolean(),"breathing alignment fixture calls the original Start method");
                foreach(var item in alignmentFixture.RootElement.GetProperty("cases").EnumerateArray())
                {
                    var settings=new AppleMusicSettings{RightAlignFirst=item.GetProperty("rightFirst").GetBoolean(),BreathingDotsAlignmentLeft=item.GetProperty("forceLeft").GetBoolean()};
                    var alignment=item.GetProperty("alignment");
                    _dots.SetAlignment(alignment.ValueKind==JsonValueKind.Null?null:Enum.Parse<HorizontalAlignment>(alignment.GetString()!),settings);
                    Check(_dots.HorizontalAlignment==Enum.Parse<HorizontalAlignment>(item.GetProperty("result").GetString()!)&&_dots.RenderTransformOrigin==new Point(item.GetProperty("originX").GetDouble(),item.GetProperty("originY").GetDouble()),"breathing alignment and transform origin match original Start "+item);
                }
            }
            WordEdgeChecks.Run(Check,folder);
            LineAnimationChecks.Run(Check);
            BackgroundAnimationChecks.Run(Check);
            await BreathingPlaybackChecks.RunAsync(Check);
            await WordResetChecks.RunAsync(Check);
            ReflowChecks.Run(Check);
            PlusLighterChecks.Run(Check);
            IsolationEffectChecks.Run(Check);
            IsolationPaletteChecks.Run(Check);
            var isolationOptions=new ViewOptions{Background=BackgroundMode.Isolation};
            var isolatedView=new BackgroundView();await isolatedView.SetModeAsync(BackgroundMode.Isolation,isolationOptions);await isolatedView.SetArtworkAsync(Artwork.Create(0),"palette-test");
            Check(isolatedView.Children.OfType<IsolationBackground>().Count()==1&&isolatedView.LastError is null,"Isolation setting creates its real renderer and accepts an artwork palette");isolatedView.Dispose();
            Check(isolatedView.Children.Count==0,"Isolation mode releases its detached view on close");
            var staleIsolation=new BackgroundView();await staleIsolation.SetModeAsync(BackgroundMode.Isolation,isolationOptions);var pendingPalette=staleIsolation.SetArtworkAsync(Artwork.Create(1),"stale-palette");staleIsolation.Dispose();await pendingPalette;
            Check(staleIsolation.Children.Count==0,"late palette completion cannot recreate a closed renderer");
            var backgroundOptions=new ViewOptions();var backgroundSettings=backgroundOptions.AppleMusicLyrics.BackgroundAppleMusicOriginal;
            backgroundSettings.FrameRateLimit=48;backgroundSettings.RenderScale=.75;backgroundSettings.BassPulseScale=2.3;backgroundSettings.BlurScale=.6;backgroundSettings.PortraitControlPointCount=7;backgroundSettings.PortraitSubdivisionLevels=3;backgroundSettings.LandscapeControlPointCount=8;backgroundSettings.LandscapeSubdivisionLevels=4;
            var librarySettings=BackgroundView.GetIosSettings(backgroundOptions);
            Check(librarySettings.FrameRateLimit==48&&librarySettings.RenderScale==.75&&librarySettings.BlurScale==.6&&librarySettings.PortraitControlPointCount==7&&librarySettings.PortraitSubdivisionLevels==3&&librarySettings.LandscapeControlPointCount==8&&librarySettings.LandscapeSubdivisionLevels==4,"all original background parameters reach the source renderer settings");
            Check(librarySettings.BassPulseScale==0,"system-audio pulse remains opt-in");backgroundOptions.AudioReactive=true;
            Check(BackgroundView.GetIosSettings(backgroundOptions).BassPulseScale==2.3,"enabled audio pulse uses the configured scale");
            _reference.UpdateLegacy(0,false,true);
            Check(Math.Abs(_reference.Get<BlurEffect>("WebImageBackgroundBlurEffect").Radius-160*VisualTreeHelper.GetDpi(_reference.View).PixelsPerInchX/192)<.01,"static backdrop uses the loaded reference DPI blur override");
            Check(_reference.Get<Grid>("GridBackgroundSub1").Opacity==1&&_reference.Get<Grid>("GridBackgroundSub2").Visibility==Visibility.Hidden,"static backdrop uses a single fixed layer");
            _reference.UpdateLegacy(0,false);
            await LoadSongAsync(0);
            using(var waitingSession=new SystemMediaConnection(Dispatcher))
            {
                _system=waitingSession;int generation=_loadGeneration;
                try
                {
                    _clock.Play();_clock.Seek(_clock.DurationMs);EndTrack();Check(_loadGeneration==generation,"temporarily missing system media session does not advance the local library");
                    _clock.Pause();_clock.Seek(16000);Seek(1000);Check(_clock.PositionMs==16000,"missing remote session cannot seek the local lyric clock");
                    TogglePlay();Check(!_clock.IsPlaying,"rejected remote playback cannot start the local clock");
                    Next();Previous();Check(_loadGeneration==generation&&_clock.PositionMs==16000,"missing remote next or previous cannot select or rewind local demo songs");
                    int rejected=0;
                    foreach(var command in new Func<Task>[]{waitingSession.PlayAsync,waitingSession.PauseAsync,()=>waitingSession.SeekAsync(1000),waitingSession.NextAsync,waitingSession.PreviousAsync,()=>waitingSession.ShuffleAsync(true),()=>waitingSession.RepeatAsync(MediaPlaybackAutoRepeatMode.Track)})
                        try{await command();}catch(InvalidOperationException){rejected++;}
                    Check(rejected==7,"all remote transport methods reject a missing session explicitly");
                    bool shuffleBefore=_shuffle,repeatBefore=_repeat;ToggleShuffle();ToggleRepeat();Check(_shuffle==shuffleBefore&&_repeat==repeatBefore,"rejected remote shuffle and repeat commands do not toggle local state");
                    double noticeUntil=_noticeUntil;string noticeText=_status.Text;
                    ApplySystemMediaSnapshot(waitingSession,new(_song.Title,"external artist spelling","",15000,_clock.DurationMs,false));
                    Check(_title.Text==_song.Title&&_artist.Text==_song.Artist&&_clock.PositionMs==15000,"matching media metadata preserves imported title and canonical artist while applying time");
                    Check(!_reference.Get<ImageButton>("ImageClickableShuffleButton").IsEnabled&&!_reference.Get<ImageButton>("ImageClickableRepeatButton").IsEnabled,"missing or unsupported remote shuffle and repeat controls stay disabled");
                    ApplySystemMediaSnapshot(waitingSession,new(_song.Title,"external artist spelling","",15000,_clock.DurationMs,false,ShuffleActive:true,RepeatMode:MediaPlaybackAutoRepeatMode.Track,CanShuffle:true,CanRepeat:true));
                    Check(_shuffle&&_repeat&&ReferenceEquals(_reference.Get<ImageButton>("ImageClickableRepeatButton").ImageSource,Application.Current.Resources["Apple.Music.RepeatOneButton"]),"remote playback snapshot controls shuffle highlight and the original repeat-one icon");
                    Check(_noticeUntil==noticeUntil&&_status.Text==noticeText,"routine media polling does not replace or perpetually extend a transient notice");
                    ApplySystemMediaSnapshot(waitingSession,new("different song","different artist","",1000,_clock.DurationMs,true));
                    Check(!_clock.IsPlaying&&_clock.PositionMs==15000&&_title.Text==_song.Title,"different media track cannot play stale lyrics or relabel their metadata");
                    var oldCover=_artwork.Source;var mediaCover=Artwork.Encode(Artwork.Create(1));
                    await UpdateMediaArtworkAsync(waitingSession,mediaCover);
                    var refreshedCover=_artwork.Source;
                    Check(!ReferenceEquals(oldCover,refreshedCover)&&ReferenceEquals(refreshedCover,_smallArtwork.Source)&&ReferenceEquals(refreshedCover,_reference.Get<ArtworkImage>("WebImageBackground").Source),"media artwork reaches main cover, compact cover and bound backdrop");
                    await UpdateMediaArtworkAsync(waitingSession,mediaCover);
                    Check(ReferenceEquals(refreshedCover,_artwork.Source),"polling the same thumbnail does not decode or rebuild the cover repeatedly");
                    using var retiredSession=new SystemMediaConnection(Dispatcher);await UpdateMediaArtworkAsync(retiredSession,Artwork.Encode(Artwork.Create(2)));
                    Check(ReferenceEquals(refreshedCover,_artwork.Source),"retired media connection cannot replace the current artwork");
                }
                finally{_system=null;_clock.Pause();}
            }
            await MediaArtworkChecks.RunAsync(Check);
            int artworkSongIndex=_songIndex;string? importedCoverPath=_song.CoverPath;
            string explicitCoverPath=Path.Combine(folder,"explicit-cover.png");File.WriteAllBytes(explicitCoverPath,Artwork.Encode(Artwork.Create(2)));
            try
            {
                _song.CoverPath=explicitCoverPath;await LoadSongAsync(artworkSongIndex);
                using var artworkSession=new SystemMediaConnection(Dispatcher);_system=artworkSession;
                var explicitCover=_artwork.Source;await UpdateMediaArtworkAsync(artworkSession,Artwork.Encode(Artwork.Create(1)));
                Check(ReferenceEquals(explicitCover,_artwork.Source)&&ReferenceEquals(explicitCover,_smallArtwork.Source)&&ReferenceEquals(explicitCover,_reference.Get<ArtworkImage>("WebImageBackground").Source),"system thumbnails cannot replace an explicitly loaded cover or its backdrop");
            }
            finally{_system=null;_song.CoverPath=importedCoverPath;await LoadSongAsync(artworkSongIndex);}
            foreach(var size in new[]{new Size(1240,820),new Size(700,1000),new Size(1600,900)})
            {
                _root.Width=size.Width;_root.Height=size.Height;
                for(int pass=0;pass<3;pass++){Layout();RebuildRows();Layout();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);}
                _clock.Seek(16000);OnRendering(null,EventArgs.Empty);Layout();Follow(_anchor);_scroll.ScrollToVerticalOffset(_scrollTo);Layout();_autoScrolling=false;
                Check(_rows.Count>0,"lyrics were populated");
                Check(_portrait==(size.Height/size.Width>.85),"aspect ratio selects layout");
                Check(_scroll.ActualWidth>100&&_scroll.ViewportHeight>100,"lyrics viewport has a finite size");
                int gapAnchor=Timeline.Anchor(_groups,54000+_options.TimelineOffsetMs);
                Follow(gapAnchor,true);double gapTarget=_scrollTo;Follow(gapAnchor+1);double nextTarget=_scrollTo;
                double reserved=_portrait?83*_displayScale:43.5*_displayScale+_rows[gapAnchor+1].Host.ActualHeight/2;
                Check(Math.Abs(nextTarget-gapTarget-reserved)<1e-6,"interlude scroll uses the next lyric row and the original breathing-space offset");
                var pulled=_rows[gapAnchor+1].Pull;double originalPull=pulled.Y;pulled.BeginAnimation(TranslateTransform.YProperty,null);pulled.Y=120;
                Follow(gapAnchor,true);Check(Math.Abs(_scrollTo-gapTarget)<1e-6,"interlude target ignores the current pulling render transform instead of feeding it back into scroll");
                pulled.Y=originalPull;Follow(_anchor);_scroll.ScrollToVerticalOffset(_scrollTo);Layout();_autoScrolling=false;
                CheckPageMotion(Check);Layout();
                Check(PresentationSource.FromVisual(_root) is null&&new System.Windows.Interop.WindowInteropHelper(this).Handle==IntPtr.Zero,"render has no window handle");
                if(!_portrait)
                {
                    var controls=_reference.Get<Grid>("GridHorizontalControls");
                    Check(_seek.TranslatePoint(new Point(),_root).Y<controls.TranslatePoint(new Point(),_root).Y,"progress is above playback controls");
                    Check(Math.Abs(_reference.Get<Grid>("GridArtwork").ActualWidth-330)<.01,"artwork source size is 330");
                    Check(_reference.Get<Grid>("GridLyricsMaskHost").OpacityMask is null,"landscape has no added vertical fade");
                }
                else
                {
                    Check(_verticalControls.Visibility==Visibility.Hidden&&_smallInfo.Visibility==Visibility.Visible,"portrait starts with compact information and hidden controls");
                    SetVerticalControls(true);Layout();
                    Check(_verticalControls.ActualHeight<=size.Height/3+.1,"portrait controls occupy at most one third of the page");
                    Check(_reference.Get<Grid>("GridLyricsMaskHost").OpacityMask is LinearGradientBrush fade&&fade.GradientStops.Count==4&&fade.GradientStops[2].Offset<1,"shown controls fade the lyrics beneath them");
                }
                var image=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);image.Render(_root);
                string name=$"{size.Width:0}x{size.Height:0}";File.WriteAllBytes(Path.Combine(folder,name+".png"),Artwork.Encode(image));
                layouts.Add(new{size,portrait=_portrait,scale=_displayScale,scroll=_scroll.VerticalOffset,target=_scrollTo,viewport=new{width=_scroll.ViewportWidth,height=_scroll.ViewportHeight},artwork=Bounds(_artwork),progress=Bounds(_seek),firstActiveRow=Bounds(_rows[Math.Clamp(_anchor,0,_rows.Count-1)].Host)});
            }
            var runningBeforeResize=_rows.Select(row=>(row.Main.RunningWord,row.Main.FinishingWords)).ToArray();
            _root.Width=1500;Layout();RebuildRows();Layout();
            Check(_rows.Select(row=>(row.Main.RunningWord,row.Main.FinishingWords)).SequenceEqual(runningBeforeResize),"actual page resizing preserves each word worker's selection and finishing state");
            bool initialLineStyle=_options.AppleMusicLyrics.LineSwitchingAppleIosStyle164;_options.AppleMusicLyrics.LineSwitchingAppleIosStyle164=!initialLineStyle;RebuildRows();Layout();
            Check(_rows.All(row=>row.Main.SingOpacityDuration==_singOpacityDuration&&row.Backgrounds.All(back=>back.SingOpacityDuration==_singOpacityDuration)),"line stop duration keeps the original startup capture when settings rebuild rows");
            _options.AppleMusicLyrics.LineSwitchingAppleIosStyle164=initialLineStyle;RebuildRows();Layout();
            var originalFontPreset=_options.AppleMusicLyrics.FontPreset;string originalFontFamily=_options.AppleMusicLyrics.FontFamily;int? originalMobileStyle=_options.AppleMusicLyrics.IosStylePreset;
            foreach(var preset in new[]{FontPresets.Default,FontPresets.SpotifyMix,FontPresets.SpotifyCircular,FontPresets.SFPro,FontPresets.Custom})
                foreach(int? mobile in new int?[]{null,16,17})
                {
                    _options.AppleMusicLyrics.FontPreset=preset;_options.AppleMusicLyrics.FontFamily="Consolas";_options.AppleMusicLyrics.IosStylePreset=mobile;
                    _root.Width=700;_root.Height=1000;Layout();RebuildRows();Layout();
                    string family=ReferencePageView.FontFor(preset,"Consolas").Source;
                    Check(_reference.View.FontFamily.Source==family&&_rows[0].Main.Children.OfType<UserControl>().Single().FontFamily.Source==family,"font preset reaches the actual page and lyric row "+preset+" / "+mobile);
                    Check(_seek.FontFamily.Source==(preset==FontPresets.SFPro?ReferencePageView.AppleTextFont().Source:family)&&_seekVertical.FontFamily.Source==family,"SF Pro horizontal time bar uses Text while the vertical bar inherits the page family "+preset+" / "+mobile);
                    string mobileFont=mobile.HasValue?ReferencePageView.AppleTextFont().Source:family;
                    Check(_smallTitle.FontFamily.Source==mobileFont&&_smallArtist.FontFamily.Source==mobileFont,"mobile layout font override and default inheritance follow original resource 60148 "+preset+" / "+mobile);
                }
            _options.AppleMusicLyrics.FontPreset=originalFontPreset;_options.AppleMusicLyrics.FontFamily=originalFontFamily;_options.AppleMusicLyrics.IosStylePreset=originalMobileStyle;
            _root.Width=1600;_root.Height=900;Layout();RebuildRows();Layout();
            var originalLyricsParent=_lyricHost.Parent;var originalTrackParent=_reference.Get<Grid>("GridTrackInfo").Parent;
            var originalStageOrder=_stage.Children.Cast<UIElement>().ToArray();
            foreach(var size in new[]{new Size(1100,713),new Size(700,1000)})
            {
                _root.Width=size.Width;_root.Height=size.Height;Layout();RebuildRows();Layout();
                Rect originalBounds=_lyricHost.TransformToAncestor(_root).TransformBounds(new Rect(_lyricHost.RenderSize));
                foreach(var preset in new[]{PlusLighterEffectPresetTypes.iOS18,PlusLighterEffectPresetTypes.iOS26,PlusLighterEffectPresetTypes.Android})
                {
                    _options.AppleMusicLyrics.EnablePlusLighter=true;_options.AppleMusicLyrics.PlusLighterEffectPreset=preset;
                    for(int pass=0;pass<3;pass++){RebuildRows();Layout();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);}
                    Check(_blend.Active&&!ReferenceEquals(_lyricHost.Parent,originalLyricsParent),"blend mode composes the actual lyric tree");
                    Check(_reference.View.IsAncestorOf(_seek)&&_reference.View.IsAncestorOf(_lyricHost),"blend reparenting retains controls in the live page tree");
                    Rect blendedBounds=_lyricHost.TransformToAncestor(_root).TransformBounds(new Rect(_lyricHost.RenderSize));
                    Check(Math.Abs(originalBounds.X-blendedBounds.X)<1&&Math.Abs(originalBounds.Width-blendedBounds.Width)<1,"blend keeps the lyric viewport position and width");
                    Save($"blend-{(int)size.Width}-{preset}");
                }
                _options.AppleMusicLyrics.EnablePlusLighter=false;RebuildRows();Layout();
                Check(!_blend.Active&&ReferenceEquals(_lyricHost.Parent,originalLyricsParent)&&ReferenceEquals(_reference.Get<Grid>("GridTrackInfo").Parent,originalTrackParent),"disabling blend restores original parent slots");
                Check(_stage.Children.Cast<UIElement>().SequenceEqual(originalStageOrder),"blend disable restores exact page sibling ordering");
            }
            _root.Width=1600;_root.Height=900;Layout();RebuildRows();Layout();
            Seek(6850);OnRendering(null,EventArgs.Empty);Check(_rows[0].Main.Frame.ContentScale>1,"timeline compensation applies to the row state before the raw timestamp");
            double preparedStart=_groups[0].StartMs,oldLead=_options.TimelineOffsetMs;_options.TimelineOffsetMs=oldLead+100;RebuildRows();
            Check(_groups[0].StartMs==preparedStart,"user lyric lead does not cancel itself inside native row preparation");_options.TimelineOffsetMs=oldLead;RebuildRows();
            Seek(11000);Check(Math.Abs(_clock.PositionMs-11000)<1,"rewind uses the absolute clock");Seek(18000);Seek(10000);Check(Math.Abs(_clock.PositionMs-10000)<1,"consecutive seeks keep the last position");
            _magicController.Preview(true,MagicAction.Maximize);Layout();
            Check(Math.Abs(_reference.Get<Border>("BorderMagicButtonInside").ActualWidth-30)<.01&&Math.Abs(_reference.Get<Border>("BorderMagicButtonInside").ActualHeight-30)<.01,"magic strip uses the release hover dimensions");
            Check(_reference.Get<SymbolIcon>("SymbolMagicButton").Text==char.ConvertFromUtf32(62771),"magic strip uses the release Fluent glyph");
            var notice=_reference.Get<Grid>("GridMagicStripFeatureNotice");notice.Opacity=1;Layout();
            Check(notice.Children.OfType<Border>().Count(b=>b.Visibility==Visibility.Visible)==1,"magic notice layouts are mutually exclusive");
            Save("magic-hover");_magicController.Preview(false,MagicAction.None);
            _clock.Seek(54000);OnRendering(null,EventArgs.Empty);Layout();
            Check(_dots.Frame.Visible&&_dots.Frame.First>_dots.Frame.Second,"breathing dots brighten in order");
            var pausedDots=_dots.Frame;await Task.Delay(30);OnRendering(null,EventArgs.Empty);Check(_dots.Frame==pausedDots,"deterministic breathing preview keeps a fixed song-time sample");
            _scroll.ScrollToVerticalOffset(_scrollTo);Layout();Save("breathing");_clock.Seek(52000);OnRendering(null,EventArgs.Empty);Check(_dots.Frame.First<pausedDots.First,"rewind reconstructs the earlier breathing frame");
            _clock.Seek(60000);OnRendering(null,EventArgs.Empty);Check(_dots.Visibility==Visibility.Collapsed,"breathing dots leave when lyrics resume");
            Check(PresentationSource.FromVisual(_root) is null&&new System.Windows.Interop.WindowInteropHelper(this).Handle==IntPtr.Zero,"dynamic previews created no window handle");
            await LoadSongAsync(1);Layout();RebuildRows();Layout();Check(_rows.Any(r=>r.Backgrounds.Count>0),"background vocals are populated");
            _clock.Seek(21000);OnRendering(null,EventArgs.Empty);Layout();
            var vocalGroup=_rows.First(r=>r.Backgrounds.Count>0);
            Check(vocalGroup.Backgrounds.All(r=>r.Frame.RootOpacity>.6&&r.Frame.RootOpacity<.68),"backing rows use the original opacity target");
            Check(Math.Abs(vocalGroup.Main.Frame.ContentScale-1.02)<.001,"timed main row uses the original highlight scale");
            Check(vocalGroup.Backgrounds.All(r=>vocalGroup.Main.IsAncestorOf(r)),"backing rows are inside the native main text container");
            int vocalIndex=_rows.IndexOf(vocalGroup);Follow(vocalIndex,true);double beforeBackgroundSpace=_scrollTo;
            var originalMargins=vocalGroup.Backgrounds.Select(back=>back.Margin).ToArray();
            for(int i=0;i<vocalGroup.Backgrounds.Count;i++){var back=vocalGroup.Backgrounds[i];var margin=back.Margin;back.Margin=new Thickness(margin.Left,margin.Top+17,margin.Right,margin.Bottom);}
            Layout();Follow(vocalIndex,true);
            Check(Math.Abs(_scrollTo-beforeBackgroundSpace)<1e-6,"interlude target subtracts the preceding background-vocal layout space");
            for(int i=0;i<vocalGroup.Backgrounds.Count;i++)vocalGroup.Backgrounds[i].Margin=originalMargins[i];
            Layout();Follow(_anchor,_breathing);_autoScrolling=false;
            double openHeight=vocalGroup.Host.ActualHeight;
            _scroll.ScrollToVerticalOffset(_scrollTo);Layout();Save("duet-active");
            _clock.Seek(40000);OnRendering(null,EventArgs.Empty);Layout();Check(vocalGroup.Backgrounds.All(r=>r.Opacity==0),"inactive backing rows fade to zero");
            Check(vocalGroup.Host.ActualHeight<openHeight-vocalGroup.Backgrounds.Sum(r=>r.NaturalHeight)*.8,"backing rows return their layout space when inactive");
            _clock.Seek(20000);OnRendering(null,EventArgs.Empty);Layout();Check(vocalGroup.Backgrounds.All(r=>r.Opacity>.5),"rewind restores backing rows from absolute time");
            Check(Math.Abs(vocalGroup.Host.ActualHeight-openHeight)<1,"rewind restores the same group height");
            Check(_rows.All(r=>r.Pull.Y==0),"discontinuous seek clears stale pulling transforms");
            double committed=_clock.PositionMs;int commits=0;void CountCommit(double _)=>commits++;
            _seek.SeekRequested+=CountCommit;_seek.IsRealTimeUpdate=false;
            _seek.BeginPreview(200);Check(_seek.Dragging&&commits==0&&Math.Abs(_clock.PositionMs-committed)<1,"seek preview does not submit playback");
            _seek.MovePreview(-20);Check(_seek.CurrentTime==0&&_seek.ElasticOffset<0,"seek left overflow stretches the release bar");
            _seek.CancelPreview();Check(!_seek.Dragging&&commits==0&&_seek.ElasticOffset==0,"seek cancel resets stretch without submitting");
            _seek.BeginPreview(2000);Check(_seek.CurrentTime==_seek.TotalTime&&_seek.ElasticOffset>0,"seek right overflow clamps time and stretches");
            _seek.CommitPreview();Check(commits==1&&Math.Abs(_clock.PositionMs-_clock.DurationMs)<1,"seek release submits once");
            _seek.CommitPreview();Check(commits==1,"idle seek release does not submit");
            _seek.IsRealTimeUpdate=true;_seek.BeginPreview(100);Check(commits==2,"real time seek submits during drag");_seek.CancelPreview();_seek.IsRealTimeUpdate=false;_seek.SeekRequested-=CountCommit;
            _seekVertical.BeginPreview(100);_seekVertical.CommitPreview();Check(Math.Abs(_clock.PositionMs-_seek.CurrentTime)<1&&_seek.CurrentTime==_seekVertical.CurrentTime,"portrait preview shares time with the primary bar");
            Check(new System.Windows.Interop.WindowInteropHelper(this).Handle==IntPtr.Zero,"seek preview tests created no window");
            var timer=new AnimatedTimerReadout{FontSize=26,Animate=true};timer.Measure(new Size(200,60));timer.Arrange(new Rect(0,0,200,60));timer.UpdateLayout();
            timer.Time=TimeSpan.FromSeconds(9);timer.Time=TimeSpan.FromSeconds(10);
            Check(timer.Slots[4].Value=='1'&&timer.Slots[5].Value=='0'&&timer.Slots[5].DelayMs==110,"rolling time accepts the native second carry animation");
            timer.Time=TimeSpan.FromMilliseconds(-1);Check(timer.Slots[0].Value=='-'&&timer.Slots[0].Visible,"rolling time accepts the remaining-time sign transition");
            timer.Time=TimeSpan.FromMinutes(120);timer.UpdateLayout();Check(timer.Text=="120:00","long tracks retain their full time");
            Check(PresentationSource.FromVisual(timer) is null,"rolling time preview stays disconnected from a window");
            var favorite=new FavoriteButton{Width=28,Height=28};var favoriteView=(UserControl)favorite.Content;
            var click=typeof(FavoriteButton).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var animationField=typeof(FavoriteButton).GetField("_favoriteAnimation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            click.Invoke(favorite,null);var favoriteAnimation=(Storyboard)animationField.GetValue(favorite)!;favoriteAnimation.Pause(favorite);
            favoriteAnimation.SeekAlignedToLastTick(favorite,TimeSpan.FromMilliseconds(125),TimeSeekOrigin.BeginTime);
            Check(favorite.IsFavorited&&Math.Abs(((Border)favoriteView.FindName("BorderIcon")).Opacity-.5)<.001&&Math.Abs(((Border)favoriteView.FindName("BorderIconFilled")).Opacity-.3)<.001,"favorite crossfade follows the recovered 100 ms wait and 50 ms fade");
            favoriteAnimation.SeekAlignedToLastTick(favorite,TimeSpan.FromMilliseconds(400),TimeSeekOrigin.BeginTime);
            double favoriteAlpha=((Border)favoriteView.FindName("BorderIconFilled")).Opacity,favoriteScale=((ScaleTransform)favoriteView.FindName("BorderIconFilled_ScaleTransform")).ScaleX;
            Check(Math.Abs(favoriteAlpha-.85)<.001&&favoriteScale>1.37,"favorite reaches its release glow and expansion");
            favoriteAnimation.SeekAlignedToLastTick(favorite,TimeSpan.FromMilliseconds(750),TimeSeekOrigin.BeginTime);
            Check(Math.Abs(((Border)favoriteView.FindName("BorderIconFilled")).Opacity-.6)<.001&&((DropShadowEffect)favoriteView.FindName("BorderIconFilled_DropShadowEffect")).Opacity<.001,"favorite glow fades during the recovered spring return");
            favoriteAnimation.SeekAlignedToLastTick(favorite,TimeSpan.FromMilliseconds(1100),TimeSeekOrigin.BeginTime);
            Check(((Border)favoriteView.FindName("BorderIconFilled")).Opacity<.001&&Math.Abs(((Border)favoriteView.FindName("BorderIconFilledReversed")).Opacity-.5)<.001&&Math.Abs(((ScaleTransform)favoriteView.FindName("BorderIconFilled_ScaleTransform")).ScaleX-1)<.001,"favorite finishes in the native reversed-star state");
            click.Invoke(favorite,null);click.Invoke(favorite,null);favorite.IsFavorited=false;
            Check(animationField.GetValue(favorite) is null&&((Border)favoriteView.FindName("BorderIcon")).Opacity==1&&((Border)favoriteView.FindName("BorderIconFilledReversed")).Opacity==0,"rapid favorite reversal cancels the old animation");
            click.Invoke(favorite,null);favorite.ResetVisual();Check(animationField.GetValue(favorite) is null&&favorite.IsFavorited,"track reload clears animation even when favorite state stays equal");
            click.Invoke(favorite,null);click.Invoke(favorite,null);favorite.MotionEnabled=false;
            Check(animationField.GetValue(favorite) is null&&((Border)favoriteView.FindName("BorderIconFilledReversed")).Opacity==.5,"reducing motion cancels an in-flight favorite animation");
            favorite.SetPressedVisual(true);await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(Math.Abs(((ScaleTransform)favoriteView.FindName("GridMain_ScaleTransform")).ScaleX-.85)<.001,"favorite press uses the native scale with reduced motion");
            favorite.SetPressedVisual(false);Check(PresentationSource.FromVisual(favorite) is null,"favorite animation checks stay disconnected from a window");
            _clock.Play();OnRendering(null,EventArgs.Empty);Check(_artworkScale.ScaleX==1&&_artworkScale.ScaleY==1,"playing artwork uses the release full scale");
            _clock.Pause();OnRendering(null,EventArgs.Empty);Check(_artworkScale.ScaleX==.75&&_artworkScale.ScaleY==.75,"paused artwork uses the release reduced scale");
            _reference.UpdateLegacy(25,false);
            Check(Math.Abs(_reference.Get<ScaleTransform>("GridBackgroundScaleTransform").ScaleX-2.6)<.001,"legacy source transform reaches its scale turning point");
            Check(Math.Abs(_reference.Get<TranslateTransform>("GridBackgroundTranslateTransform").Y-170)<.001,"legacy source transform reaches its motion turning point");
            _reference.UpdateLegacy(0,false);
            var nativeMenu=_reference.Get<ContextMenu>("MenuRightClick");
            Check(ReferenceEquals(ContextMenu,nativeMenu)&&nativeMenu.Items.OfType<MenuItem>().Take(7).All(m=>m.Header is string text&&!string.IsNullOrWhiteSpace(text)),"native page menu labels and seven commands are connected");
            var themedMenu=new ContextMenu();ReferenceMenus.Apply(themedMenu);themedMenu.ApplyTemplate();
            ReferenceMenus.UpdateTheme(true);themedMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Check(((SolidColorBrush)themedMenu.Background).Color==Color.FromRgb(243,243,243)&&((SolidColorBrush)_reference.Get<ContextMenu>("MenuThreeDot").Background).Color==Color.FromArgb(232,68,68,68),"reopening a menu refreshes its theme while preserving explicit page translucency");
            ReferenceMenus.UpdateTheme(false);themedMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Check(((SolidColorBrush)themedMenu.Background).Color==Color.FromRgb(46,46,46),"an existing menu resolves the restored dark theme");
            var iconMenuItem=new IconMenuItem{Header="Menu",SymbolIcon="Library16",FontSize=14};iconMenuItem.Measure(new Size(400,100));iconMenuItem.ApplyTemplate();
            Check(iconMenuItem.Template.FindName("Border",iconMenuItem) is Border&&((SymbolIcon)iconMenuItem.Icon).Text==char.ConvertFromUtf32(59233),"derived menu items consume the recovered template and release Library16 glyph");
            iconMenuItem.IsEnabled=false;
            Check(((SolidColorBrush)iconMenuItem.Foreground).Color==Color.FromArgb(93,255,255,255),"disabled menu items consume the source theme foreground");
            double oldZoom=_options.Zoom;Check(!HandleKey(Key.OemPlus,ModifierKeys.Control,true)&&_options.Zoom==oldZoom,"text editing does not trigger lyric shortcuts");
            Check(HandleKey(Key.OemPlus,ModifierKeys.Control)&&_options.Zoom>oldZoom,"native zoom shortcut changes display scale");Layout();RebuildRows();Layout();
            HandleKey(Key.D0,ModifierKeys.Control);Layout();RebuildRows();Layout();Check(Math.Abs(_options.Zoom-1)<.001,"reset zoom restores the current window baseline");
            var originalZoomLevels=_options.AppleMusicLyrics.ZoomingLevels;
            double originalHostZoom=_options.Zoom;
            _options.AppleMusicLyrics.ZoomingLevels=new(){Horizontal=.8,Vertical=1.3,HorizontalFullscreen=.9,VerticalFullscreen=1.1};
            foreach(var scene in new (bool Portrait,bool Maximized,double Inverse)[]{(false,false,.8),(true,false,1.3),(false,true,.9),(true,true,1.1)})
            {
                _fullscreen=scene.Maximized;_root.Width=scene.Portrait?700:1240;_root.Height=scene.Portrait?1000:820;
                Layout();RebuildRows();Layout();
                Check(Math.Abs(_displayScale-1/scene.Inverse)<1e-9,"page restores the matching original zoom scene "+scene);
                HandleKey(Key.OemPlus,ModifierKeys.Control);Layout();RebuildRows();Layout();
                double changed=ZoomRules.AdjustInverse(scene.Inverse,true);
                Check(Math.Abs(_displayScale-1/changed)<1e-9&&ZoomRules.Remembered(_options.AppleMusicLyrics.ZoomingLevels,scene.Portrait,scene.Maximized)==changed,"zoom shortcut updates only its active scene "+scene);
                var persisted=SettingsFile.Load(Path.Combine(App.DataFolder,"settings.json"),_=>{});
                Check(ZoomRules.Remembered(persisted.AppleMusicLyrics.ZoomingLevels,scene.Portrait,scene.Maximized)==changed,"zoom shortcut persists its original inverse value "+scene);
            }
            _options.AppleMusicLyrics.ZoomingRestore=false;
            Layout();RebuildRows();Layout();double remembered=_options.AppleMusicLyrics.ZoomingLevels.VerticalFullscreen;
            HandleKey(Key.OemPlus,ModifierKeys.Control);Layout();RebuildRows();Layout();
            Check(_options.AppleMusicLyrics.ZoomingLevels.VerticalFullscreen==remembered&&_displayScale>1,"disabled zoom memory permits a temporary adjustment without overwriting the scene");
            _fullscreen=false;_root.Width=1240;_root.Height=820;Layout();RebuildRows();Layout();
            Check(Math.Abs(_displayScale-1/1.1)<1e-9,"disabled zoom memory returns to the source default on a scene switch");
            _options.AppleMusicLyrics.ZoomingRestore=true;_options.AppleMusicLyrics.ZoomingLevels=new();_zoomContext=-1;_options.Zoom=1.2;
            Layout();RebuildRows();Layout();
            Check(Math.Abs(_displayScale-1.2/1.1)<1e-9&&Math.Abs(_options.AppleMusicLyrics.ZoomingLevels.Horizontal-1.1/1.2)<1e-9,"legacy host zoom migrates once into the active original scene");
            _root.Width=700;_root.Height=630;Layout();RebuildRows();Layout();
            Check(_portrait&&Math.Abs(_displayScale-1/1.45)<1e-9,"another scene starts with its own default after legacy zoom migration");
            HandleKey(Key.D0,ModifierKeys.Control);Layout();RebuildRows();Layout();
            Check(_options.AppleMusicLyrics.ZoomingLevels.Vertical==1.1&&Math.Abs(_displayScale-1/1.1)<1e-9,"reset uses the original 1.0 aspect cutoff even within the 0.85 mobile layout");
            _options.AppleMusicLyrics.ZoomingRestore=true;_options.AppleMusicLyrics.ZoomingLevels=originalZoomLevels;_options.Zoom=originalHostZoom;_root.Width=1600;_root.Height=900;Layout();RebuildRows();Layout();
            bool translation=_options.Translation;HandleKey(Key.C,ModifierKeys.Alt);RefreshContextMenu();Check(_reference.Get<IconMenuItem>("MenuItemTranslation").SymbolIcon==(_options.Translation?"Checkmark12":"Empty"),"translation shortcut updates the native menu state");
            HandleKey(Key.C,ModifierKeys.Alt);Check(_options.Translation==translation,"translation shortcut is reversible");
            _songs.Add(LyricsFile.ParseLrc("Unscheduled first line\nUnscheduled second line"));await LoadSongAsync(_songs.Count-1);
            for(int pass=0;pass<3;pass++){Layout();RebuildRows();Layout();}
            _clock.Seek(1000);OnRendering(null,EventArgs.Empty);Layout();
            Check(_anchor==-1&&_rows.All(r=>r.Main.Frame.ContentScale==1&&Math.Abs(r.Main.Frame.PlayedAlpha-.3)<.001),"plain text remains static without auto highlighting");Save("plain-text");
            _songs.Add(new Song{Id="empty-fixture",Title="Empty lyrics",Artist="Lyric Lab",DurationMs=5000});await LoadSongAsync(_songs.Count-1);
            for(int pass=0;pass<3;pass++){Layout();RebuildRows();Layout();}
            OnRendering(null,EventArgs.Empty);Layout();
            Check(_rows.Count==0&&_reference.Get<ColumnDefinition>("ColumnDefinitionRightSide").ActualWidth==0&&_infoView.Visibility==Visibility.Visible,"empty lyrics collapse the text column");
            var albumBounds=_reference.Get<Grid>("GridArtwork").TransformToAncestor(_root).TransformBounds(new Rect(_reference.Get<Grid>("GridArtwork").RenderSize));
            Check(Math.Abs(albumBounds.X+albumBounds.Width/2-_root.Width/2)<1,"empty lyrics center the album information");Save("empty-lyrics");
            await LoadSongAsync(0);for(int pass=0;pass<3;pass++){Layout();OnRendering(null,EventArgs.Empty);Layout();}
            Check(_rows.Count>0&&_lyricHost.Visibility==Visibility.Visible&&_scroll.ActualWidth>100,"timed lyrics recover after the empty layout");
            var copyMenu=_rows[0].Main.ContextMenu;
            Check(copyMenu is not null&&copyMenu.Items.OfType<MenuItem>().Count()==7&&copyMenu.Items.OfType<MenuItem>().All(m=>m.Header is string text&&!string.IsNullOrWhiteSpace(text)),"native line menu has populated copy actions");
            string copyPayload=CopyText.Build(CopyKind.LineWithTranslation,_groups[0].Main,_song.Lines,_groups.Select(g=>g.Main));
            Check(copyPayload.Contains(_groups[0].Main.Text,StringComparison.Ordinal)&&copyPayload.Contains(_groups[0].Main.Translation,StringComparison.Ordinal),"copy text is prepared without writing the clipboard");
            RefreshTrackMenu();Check(_reference.Get<MenuItem>("MenuItemAddToLibrary").Visibility==Visibility.Visible&&_reference.Get<MenuItem>("MenuItemDeleteFromLibrary").Visibility==Visibility.Collapsed,"track menu follows local library state");
            Check(_reference.Get<ContextMenu>("MenuArtistNameClick").Items.OfType<MenuItem>().All(m=>!m.IsEnabled),"external artist navigation requires source identifiers");
            var sessionField=typeof(BackgroundView).GetField("_session",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var held=new HeldBackgroundSession();sessionField.SetValue(_background,held);
            _clock.Play();var firstNavigation=NavigateLocalAsync(1);var secondNavigation=NavigateLocalAsync(2);
            Check(held.Pending.Count==2,"two navigation artwork operations can overlap");held.Complete(1);await secondNavigation;held.Complete(0);await firstNavigation;
            Check(_song.Id=="demo-syllable"&&_clock.IsPlaying,"stale navigation cannot pause the current track");
            var thirdNavigation=NavigateLocalAsync(0);var fourthNavigation=NavigateLocalAsync(1);TogglePlay();held.Complete(3);await fourthNavigation;held.Complete(2);await thirdNavigation;
            Check(_song.Id=="demo-duet"&&!_clock.IsPlaying,"pause during navigation is preserved by late completions");
            sessionField.SetValue(_background,null);held.Dispose();
            var closedBackground=new BackgroundView();closedBackground.Dispose();await closedBackground.SetModeAsync(BackgroundMode.Legacy,_options);Check(closedBackground.Children.Count==0,"disposed background cannot recreate child renderers");
            string assemblyHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)));
            File.WriteAllText(Path.Combine(folder,"verification.json"),JsonSerializer.Serialize(new{passed=true,verifiedAtUtc=DateTimeOffset.UtcNow,assemblySha256=assemblyHash,assertions=checks.Count,checks,layouts,scope="Disconnected WPF visual tree; no Window.Show, Application.Run or desktop input; legacy background only."},LyricsFile.JsonOptions));
        }
        finally{_clock.Pause();_audio.Close();StopArtworkVideo();_dots.Dispose();_magicController.Dispose();_background.Dispose();ReleaseSleepRequest();_disposed=true;}
        void Layout(){_root.Measure(new Size(_root.Width,_root.Height));_root.Arrange(new Rect(0,0,_root.Width,_root.Height));_root.UpdateLayout();}
        object Bounds(FrameworkElement element){var bounds=element.TransformToAncestor(_root).TransformBounds(new Rect(element.RenderSize));return new{x=bounds.X,y=bounds.Y,width=bounds.Width,height=bounds.Height};}
        void Save(string name){var image=new RenderTargetBitmap((int)_root.Width,(int)_root.Height,96,96,PixelFormats.Pbgra32);image.Render(_root);File.WriteAllBytes(Path.Combine(folder,name+".png"),Artwork.Encode(image));}
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks.Add(label);}
    }
    private sealed class HeldBackgroundSession : Lyricify.Backgrounds.IBackgroundSession
    {
        public List<TaskCompletionSource> Pending{get;}=[];
        public bool IsReady=>true;
        public event EventHandler FirstFramePresented{add{}remove{}}
        public event EventHandler<Lyricify.Backgrounds.BackgroundFaultedEventArgs> Faulted{add{}remove{}}
        public void UpdateState(Lyricify.Backgrounds.BackgroundState state){}
        public Task SetArtworkAsync(Lyricify.Backgrounds.BackgroundArtwork artwork,CancellationToken cancellationToken=default){var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Pending.Add(completion);return completion.Task;}
        public void Complete(int index)=>Pending[index].SetResult();
        public void Dispose(){}
    }
}

