using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Lyricify.Backgrounds;
using Lyricify.Backgrounds.AppleMusicInspired.Ios;
using Lyricify.Backgrounds.AppleMusicInspired.Ios.Wpf;
using Lyricify.Backgrounds.AppleMusicInspired.IosClassic;
using Lyricify.Backgrounds.AppleMusicInspired.IosClassic.Wpf;
using Cliric.Core;
using Cliric.App.Reference;

namespace Cliric.App;

public sealed class BackgroundView : Grid, IDisposable
{
    private IBackgroundSession? _session;
    private BackgroundMode _mode = (BackgroundMode)(-1);
    private readonly Image _first = new() { Stretch = Stretch.UniformToFill, Opacity = .7 };
    private readonly Image _second = new() { Stretch = Stretch.UniformToFill, Opacity = .5 };
    private readonly Grid _legacy = new() { ClipToBounds = true };
    private byte[] _encoded = [];
    private string _id = "";
    private BitmapSource? _art;
    private double _time;
    private int _generation;
    private bool _disposed;
    private FrameworkElement? _renderView;
    private bool _independent;
    private bool _inlineFrameAvailable;
    private IsolationBackground? _isolation;
    private IsolationPalettes _paletteMode;
    private int _paletteGeneration;
    private readonly RotateTransform[] _rotations = [new(), new()];
    private readonly TranslateTransform[] _moves = [new(), new()];
    public string? LastError
    {
        get; private set;
    }
    public bool RendererReady => _session?.IsReady ?? true;
    internal bool IndependentRendererActive => _independent && _session is not null;
    public event Action? Ready;
    public event Action<string>? Fault;

    public BackgroundView()
    {
        IsHitTestVisible = false;
        Background = Brushes.Black;
        ClipToBounds = true;
        _first.Effect = new BlurEffect { Radius = 120, RenderingBias = RenderingBias.Performance };
        _second.Effect = new BlurEffect { Radius = 120, RenderingBias = RenderingBias.Performance };
        int index = 0;
        foreach (var image in new[] { _first, _second })
        {
            image.RenderTransformOrigin = new Point(.5, .5);
            image.CacheMode = new BitmapCache { RenderAtScale = .6 };
            var transforms = new TransformGroup();
            transforms.Children.Add(new ScaleTransform(2, 2));
            transforms.Children.Add(_rotations[index]);
            transforms.Children.Add(_moves[index]);
            image.RenderTransform = transforms;
            _legacy.Children.Add(image);
            index++;
        }
        _legacy.Children.Add(new Border { Background = Brushes.Black, Opacity = .27 });
        Children.Add(_legacy);
    }
    public async Task SetArtworkAsync(BitmapSource art, string id)
    {
        if (_disposed)
            return;
        _art = art;
        _id = id;
        _encoded = Artwork.Encode(art);
        _first.Source = art;
        _second.Source = art;
        var session = _session;
        if (session is not null)
        {
            try
            {
                await session.SetArtworkAsync(new BackgroundArtwork(id, _encoded));
            }
            catch (Exception e) { if (ReferenceEquals(session, _session)) Fallback(e); }
        }
        if (_isolation is not null)
            await UpdateIsolationPaletteAsync();
    }
    public async Task SetModeAsync(BackgroundMode mode, ViewOptions options, bool force = false)
    {
        if (_disposed)
            return;
        if (_mode == mode && !force)
            return;
        int generation = ++_generation;
        _independent = ShouldUseIndependent(mode, options);
        _mode = mode;
        _inlineFrameAvailable = false;
        ++_paletteGeneration;
        _session?.Dispose();
        _session = null;
        _isolation?.Dispose();
        _isolation = null;
        _renderView = null;
        Children.Clear();
        LastError = null;
        if (mode is BackgroundMode.ReferenceAppleMusic or BackgroundMode.Modern or BackgroundMode.Classic)
        {
            try
            {
                FrameworkElement view;
                if (SupportsIndependent(mode))
                {
                    _session = CreateIosSession(mode, GetIosSettings(options), options.LightTheme, _independent);
                    view = (FrameworkElement)_session;
                }
                else
                {
                    var bg = new AppleMusicIosClassicBackground(new AppleMusicIosClassicBackgroundSettings { FrameRateLimit = 60, BassPulseScale = options.AudioReactive ? 1 : 0, RenderScale = .85 }, lightTheme: options.LightTheme);
                    _session = bg;
                    view = bg;
                }
                _renderView = view;
                var session = _session;
                session.FirstFramePresented += OnReady;
                session.Faulted += OnFaulted;
                if (_independent)
                    Children.Add(_legacy);
                Children.Add(view);
                if (_encoded.Length > 0)
                    await session.SetArtworkAsync(new BackgroundArtwork(_id, _encoded));
            }
            catch (Exception e) { if (!_disposed && generation == _generation) Fallback(e); }
        }
        else if (mode == BackgroundMode.Isolation)
        {
            _paletteMode = options.AppleMusicLyrics.BackgroundDynamicIsolationPalettes;
            var settings = options.AppleMusicLyrics;
            var renderer = new IsolationBackground(settings.BackgroundDynamicIsolationFrameRateLimit, settings.BackgroundDynamicIsolationRenderScale, options.LightTheme ? 0 : 20f / 255);
            _isolation = renderer;
            _renderView = renderer;
            Children.Add(renderer);
            renderer.Fault += error => Dispatcher.InvokeAsync(() => { if (ReferenceEquals(renderer, _isolation)) Fallback(error); });
            if (_encoded.Length > 0)
                await UpdateIsolationPaletteAsync();
        }
        else if (mode == BackgroundMode.Legacy)
            Children.Add(_legacy);
        else
            Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)) });
    }

    internal static bool SupportsIndependent(BackgroundMode mode) => mode is BackgroundMode.ReferenceAppleMusic or BackgroundMode.Modern;

    private bool ShouldUseIndependent(BackgroundMode mode, ViewOptions options) =>
        SupportsIndependent(mode) && options.AppleMusicLyrics.EnableIndependentBackgroundRenderer &&
        Window.GetWindow(this) is { } window &&
        (window is MainWindow main ? main.CanPresentIndependentBackground : window.WindowState == WindowState.Maximized);

    internal static IBackgroundSession CreateIosSession(BackgroundMode mode, AppleMusicIosBackgroundSettings settings,
        bool lightTheme, bool independent, int presetSlot = -1) =>
        mode == BackgroundMode.ReferenceAppleMusic
            ? ReferenceAppleMusicBackground.Create(settings, lightTheme, independent, presetSlot)
            : independent
                ? new IndependentAppleMusicIosBackground(settings, lightTheme, presetSlot: presetSlot)
                : new AppleMusicIosBackground(settings, lightTheme, presetSlot: presetSlot);
    private async Task UpdateIsolationPaletteAsync()
    {
        var renderer = _isolation;
        if (renderer is null || _encoded.Length == 0)
            return;
        int generation = ++_paletteGeneration;
        var data = _encoded;
        var mode = _paletteMode;
        try
        {
            var palette = await Task.Run(() => IsolationPalette.Extract(data, mode));
            if (!_disposed && generation == _paletteGeneration && ReferenceEquals(renderer, _isolation))
                renderer.UpdateColors(palette);
        }
        catch (Exception exception) { if (!_disposed && generation == _paletteGeneration && ReferenceEquals(renderer, _isolation)) Fallback(exception); }
    }
    internal static AppleMusicIosBackgroundSettings GetIosSettings(ViewOptions options)
    {
        var settings = options.AppleMusicLyrics.BackgroundAppleMusicOriginal;
        return new()
        {
            FrameRateLimit = settings.FrameRateLimit,
            RenderScale = settings.RenderScale,
            BassPulseScale = options.AudioReactive ? settings.BassPulseScale : 0,
            BlurScale = settings.BlurScale,
            PortraitControlPointCount = settings.PortraitControlPointCount,
            PortraitSubdivisionLevels = settings.PortraitSubdivisionLevels,
            LandscapeControlPointCount = settings.LandscapeControlPointCount,
            LandscapeSubdivisionLevels = settings.LandscapeSubdivisionLevels
        };
    }
    private void OnReady(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
            NotifyReady();
        else
            Dispatcher.InvokeAsync(NotifyReady);
        void NotifyReady()
        {
            if (_disposed || !ReferenceEquals(sender, _session))
                return;
            if (_independent)
                Children.Remove(_legacy);
            Ready?.Invoke();
        }
    }
    private void OnFaulted(object? sender, BackgroundFaultedEventArgs e) => Dispatcher.InvokeAsync(() => { if (ReferenceEquals(sender, _session)) Fallback(e.Exception); });
    private void Fallback(Exception error)
    {
        if (_disposed)
            return;
        LastError = error.GetType().Name + ": " + error.Message;
        ++_paletteGeneration;
        _session?.Dispose();
        _session = null;
        _isolation?.Dispose();
        _isolation = null;
        _renderView = null;
        Children.Clear();
        Children.Add(_legacy);
        Fault?.Invoke(LastError);
    }
    public void Update(bool playing, bool portrait, bool visible, ViewOptions options, double elapsed, bool holdInlineFrame = false)
    {
        bool independent = ShouldUseIndependent(_mode, options);
        if (independent != _independent)
        {
            _ = SetModeAsync(_mode, options, true);
            return;
        }
        if (_renderView is not null)
        {
            _renderView.Visibility = options.ReduceMotion ? Visibility.Hidden : Visibility.Visible;
            bool showBackdrop = options.ReduceMotion || IndependentRendererActive && !RendererReady;
            if (showBackdrop && !Children.Contains(_legacy))
                Children.Insert(0, _legacy);
            else if (!showBackdrop && Children.Contains(_legacy))
                Children.Remove(_legacy);
        }
        bool presenting = visible && !options.ReduceMotion;
        if (_session is { } session)
        {
            // Inline sessions keep their last texture attached when frame production pauses.
            bool hold = holdInlineFrame && !_independent && _inlineFrameAvailable;
            session.UpdateState(new BackgroundState { IsPlaying = playing && !options.ReduceMotion, IsVertical = portrait, IsVisible = presenting && !hold, IsBehindLyrics = true, IsLightTheme = options.LightTheme });
            if (!holdInlineFrame)
                _inlineFrameAvailable = presenting && _encoded.Length > 0;
        }
        if ((_session is null || options.ReduceMotion) && _mode != BackgroundMode.Solid)
        {
            if (!options.ReduceMotion)
                _time += elapsed;
            for (int i = 0; i < 2; i++)
            {
                _rotations[i].Angle = Math.Sin(_time * .06 + i * 2) * 25;
                _moves[i].X = Math.Sin(_time * .13 + i) * ActualWidth * .15;
                _moves[i].Y = Math.Cos(_time * .09 + i) * ActualHeight * .1;
            }
        }
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ++_generation;
        ++_paletteGeneration;
        _session?.Dispose();
        _session = null;
        _isolation?.Dispose();
        _isolation = null;
        _renderView = null;
        Children.Clear();
    }
}
