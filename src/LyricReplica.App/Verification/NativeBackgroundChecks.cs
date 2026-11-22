using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using LyricReplica.Core;
using LyricReplica.App.Reference;

namespace LyricReplica.App;

public sealed partial class MainWindow
{
    internal async Task VerifyPageBlendAsync(string folder)
    {
        var backdrop = _shellLegacyBackdrop!;
        var chrome = WindowChrome.GetWindowChrome(this);
        var viewport = _shell.RenderSize;
        var crop = BackdropBounds();
        double time = _legacyTime;
        if (backdrop.Parent != _shell || _background.Parent != _shell ||
            !ReferenceEquals(_reference.Get<ArtworkImage>("WebImageBackground").Source, _artwork.Source) ||
            _backdropNeutral.Opacity <= 0 || _backdropShade.Opacity <= 0)
            throw new InvalidOperationException("The two pages must share the active artwork and backdrop.");

        double early = MotionTokens.LyricsPageStaggerMs / (2d * MotionTokens.LyricsPage.Milliseconds);
        SetPageBlend(early);
        bool openingIsStaggered = _libraryTransition.Opacity is > 0 and < 1 && _lyricsTransition.Opacity == 0;
        SetPageBlend(1 - early);
        bool closingIsStaggered = _lyricsTransition.Opacity is > 0 and < 1 && _libraryTransition.Opacity == 0;
        SetPageBlend(.5);
        bool fullResolutionOverlap = _libraryTransition.Opacity > 0 && _lyricsTransition.Opacity > 0 &&
            _libraryTransition.Opacity + _lyricsTransition.Opacity < 1 &&
            _libraryTransition.CacheMode is BitmapCache { RenderAtScale: 1 } &&
            _lyricsTransition.CacheMode is BitmapCache { RenderAtScale: 1 };
        SetPageBlend(0);
        if (!openingIsStaggered || !closingIsStaggered || !fullResolutionOverlap)
            throw new InvalidOperationException("The pages must have a brief full-resolution overlap in both directions.");

        ShowLyrics();
        await Task.Delay(MotionTokens.LyricsPage.Milliseconds / 2);
        double progress = LyricsProgress;
        if (progress is <= 0 or >= 1 ||
            _backdropNeutral.Opacity is <= 0 or >= .58 || _backdropShade.Opacity is <= 0 or >= LibraryShadeOpacity ||
            _legacyTime <= time || crop != BackdropBounds() || viewport != _shell.RenderSize ||
            !ReferenceEquals(chrome, WindowChrome.GetWindowChrome(this)))
            throw new InvalidOperationException($"The staggered fades must retain full-resolution content in the same viewport. Progress={progress:F3}.");
        CaptureShell(Path.Combine(folder, "lyrics-transition.png"));
        progress = LyricsProgress;
        ShowLibrary();
        if (Math.Abs(progress - LyricsProgress) > .025)
            throw new InvalidOperationException("Reversing the page blend must continue from its current progress.");
        int settle = MotionTokens.LyricsPage.Milliseconds + 90;
        await Task.Delay(settle);
        if (LyricsProgress != 0 || _root.IsVisible || !_library!.IsVisible || _libraryTransition.Opacity != 1 || _lyricsTransition.Opacity != 0 ||
            _libraryTransition.CacheMode is not null || _lyricsTransition.CacheMode is not null)
            throw new InvalidOperationException("Returning must settle on the sharp library surface.");
        CaptureShell(Path.Combine(folder, "player-shared-background.png"));

        ShowLyrics();
        await Task.Delay(settle);
        if (LyricsProgress != 1 || !_root.IsVisible || _library.IsVisible || _libraryTransition.Opacity != 0 || _lyricsTransition.Opacity != 1 ||
            _backdropNeutral.Opacity != 0 || _backdropShade.Opacity != 0 || viewport != _shell.RenderSize)
            throw new InvalidOperationException("The settled lyric page must show the unfiltered shared backdrop.");
        CaptureShell(Path.Combine(folder, "lyrics-shared-background.png"));
        ShowLibrary();
        await Task.Delay(settle);
        if (_args.Contains("--profile-page-blend"))
            await ProfilePageBlendAsync(folder);

        Rect BackdropBounds() => backdrop.TransformToVisual(_shell).TransformBounds(new Rect(backdrop.RenderSize));
    }

    private async Task ProfilePageBlendAsync(string folder)
    {
        var frames = new List<double>();
        var openPreparation = new List<double>();
        var closePreparation = new List<double>();
        double position = _clock.PositionMs;
        bool wasPlaying = _clock.IsPlaying;
        var background = _options.Background;
        bool light = _options.LightTheme;
        bool blend = _options.AppleMusicLyrics.EnablePlusLighter;
        var profileBackground = ReferenceAppleMusicBackground.Available ? BackgroundMode.ReferenceAppleMusic : BackgroundMode.Modern;
        _options.Background = profileBackground;
        _options.LightTheme = true;
        _options.AppleMusicLyrics.EnablePlusLighter = true;
        await _background.SetModeAsync(profileBackground, _options, true);
        _rebuildPending = true;
        TimeSpan? renderingTime = null;
        long? lastFrame = null;
        void Observe(object? sender, EventArgs e)
        {
            if (e is not RenderingEventArgs frame || frame.RenderingTime == renderingTime)
                return;
            renderingTime = frame.RenderingTime;
            if (LyricsProgress is <= 0 or >= 1)
            {
                lastFrame = null;
                return;
            }
            long now = Stopwatch.GetTimestamp();
            if (lastFrame is { } previous)
                frames.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);
            lastFrame = now;
        }
        CompositionTarget.Rendering += Observe;
        try
        {
            if (!wasPlaying)
                TogglePlay();
            for (int i = 0; i < 4; i++)
            {
                Seek(12_600);
                long start = Stopwatch.GetTimestamp();
                ShowLyrics();
                openPreparation.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                await Task.Delay(800);
                start = Stopwatch.GetTimestamp();
                ShowLibrary();
                closePreparation.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                await Task.Delay(450);
                if (!_clock.IsPlaying || _clock.PositionMs < 13_400 || LyricsProgress != 0)
                    throw new InvalidOperationException("The transition must settle while the playback clock continues.");
            }
        }
        finally
        {
            CompositionTarget.Rendering -= Observe;
            if (!wasPlaying && _clock.IsPlaying)
                TogglePlay();
            Seek(position);
            _options.Background = background;
            _options.LightTheme = light;
            _options.AppleMusicLyrics.EnablePlusLighter = blend;
            await _background.SetModeAsync(background, _options, true);
            _rebuildPending = true;
        }
        frames.Sort();
        if (frames.Count == 0)
            throw new InvalidOperationException("No transition render callbacks were observed.");
        File.WriteAllText(Path.Combine(folder, "page-blend-timing.json"), JsonSerializer.Serialize(new
        {
            assemblySha256 = App.AssemblySha256, viewport = _shell.RenderSize, dpi = VisualTreeHelper.GetDpi(_shell),
            tier = RenderCapability.Tier >> 16, count = frames.Count, medianMs = frames[frames.Count / 2],
            p95Ms = frames[(int)(frames.Count * .95)], maxMs = frames[^1], playing = true,
            background = profileBackground.ToString(), lightTheme = true, plusLighter = true,
            openPreparationMs = openPreparation, closePreparationMs = closePreparation
        }, LyricsFile.JsonOptions));
    }

    private async Task CheckNativeBackgroundsAsync(string folder)
    {
        File.Delete(Path.Combine(folder, "native-backgrounds.json"));
        var results = new List<object>();
        _options.ReduceMotion = false;
        _options.LightTheme = true;
        Width = 1240;
        Height = 820;
        var modes = new List<BackgroundMode> { BackgroundMode.Isolation, BackgroundMode.Modern };
        if (ReferenceAppleMusicBackground.Available)
            modes.Add(BackgroundMode.ReferenceAppleMusic);
        foreach (var mode in modes)
        {
            _options.Background = mode;
            await _background.SetModeAsync(mode, _options, true);
            _rebuildPending = true;
            await WaitForRenderer(requireReadyEvent: mode != BackgroundMode.ReferenceAppleMusic);
            Capture(Path.Combine(folder, "native-" + mode + ".png"));
            results.Add(new
            {
                mode = mode.ToString(),
                ready = _background.RendererReady,
                error = _background.LastError,
                frames = _frameCount,
                dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this)
            });
        }
        _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer = true;
        if (_hostedByPlayer)
        {
            ShowLibrary();
            await Task.Delay(MotionTokens.LyricsPage.Milliseconds + 90);
            WindowState = WindowState.Maximized;
            await WaitForRenderer(requireReadyEvent: _options.Background != BackgroundMode.ReferenceAppleMusic);
            var renderer = _background.Children[0];
            var size = _shell.RenderSize;
            ShowLyrics();
            await Task.Delay(MotionTokens.LyricsPage.Milliseconds + 90);
            if (_background.IndependentRendererActive || _independentPresentation || !_root.IsVisible || WindowState != WindowState.Maximized ||
                !ReferenceEquals(renderer, _background.Children[0]) || _shell.RenderSize != size)
                throw new InvalidOperationException("Opening lyrics from a maximized library must retain the inline background and viewport.");
            ShowLibrary();
            await Task.Delay(MotionTokens.LyricsPage.Milliseconds + 90);
            if (_background.IndependentRendererActive || _independentPresentation || !_library!.IsVisible || WindowState != WindowState.Maximized ||
                !ReferenceEquals(renderer, _background.Children[0]) || _shell.RenderSize != size)
                throw new InvalidOperationException("Returning to a maximized library must retain the inline background and viewport.");
            WindowState = WindowState.Normal;
            ShowLyrics();
            await Task.Delay(MotionTokens.LyricsPage.Milliseconds + 90);
        }
        ToggleFullscreen();
        await _background.SetModeAsync(_options.Background, _options, true);
        bool waitingForFirstFrame = !_background.RendererReady;
        if (waitingForFirstFrame && _background.Children.Count < 2)
            throw new InvalidOperationException("Independent background startup must retain a visible artwork surface.");
        _rebuildPending = true;
        await WaitForRenderer();
        if (!_background.IndependentRendererActive || ResizeMode != ResizeMode.NoResize)
            throw new InvalidOperationException("The independent background host did not enter the native NoResize path.");
        var target = ((HwndSource)PresentationSource.FromVisual(this)).CompositionTarget;
        var foreground = new RenderTargetBitmap(8, 8, 96, 96, PixelFormats.Pbgra32);
        foreground.Render(_hostedByPlayer ? _shell : _root);
        var pixel = new byte[4];
        foreground.CopyPixels(new Int32Rect(1, 1, 1, 1), pixel, 4, 0);
        bool shellTransparent = !_hostedByPlayer || ((SolidColorBrush)_shell.Background).Color.A == 0;
        if (!_independentPresentation || !shellTransparent || ((SolidColorBrush)Background).Color.A != 0 || ((SolidColorBrush)_background.Background).Color.A != 0 || target.BackgroundColor.A != 0 || pixel[3] != 0)
            throw new InvalidOperationException($"Independent foreground state: enabled={_independentPresentation}, window={Background}, control={_background.Background}, target={target.BackgroundColor}, pixelAlpha={pixel[3]}, frames={_frameCount}.");
        results.Add(new
        {
            mode = "independent",
            rendererMode = _options.Background.ToString(),
            ready = _background.RendererReady,
            waitingForFirstFrame,
            error = _background.LastError,
            frames = _frameCount,
            resizeMode = ResizeMode.ToString(),
            foregroundAlpha = pixel[3],
            compositionAlpha = target.BackgroundColor.A
        });
        _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer = false;
        _options.Background = BackgroundMode.Legacy;
        UpdateWindowChrome();
        await _background.SetModeAsync(BackgroundMode.Legacy, _options, true);
        ToggleFullscreen();
        if (_independentPresentation || ((SolidColorBrush)Background).Color.A != 255 || ((SolidColorBrush)_background.Background).Color.A != 255)
            throw new InvalidOperationException("Leaving independent rendering did not restore an opaque foreground surface.");
        File.WriteAllText(Path.Combine(folder, "native-backgrounds.json"), JsonSerializer.Serialize(new
        {
            passed = true,
            assemblySha256 = App.AssemblySha256,
            windowShown = true,
            desktopInputSent = false,
            originalCompared = false,
            results
        }, LyricsFile.JsonOptions));
        async Task WaitForRenderer(bool requireReadyEvent = true)
        {
            await Task.Delay(200);
            for (int i = 0; requireReadyEvent && i < 100 && (!_background.RendererReady || _background.IndependentRendererActive && !_independentPresentation) && _background.LastError is null; i++)
                await Task.Delay(50);
            if (_background.LastError is { } error)
                throw new InvalidOperationException(error);
            if (requireReadyEvent && !_background.RendererReady)
                throw new InvalidOperationException("Native background did not present its first frame within 5 seconds.");
        }
    }
}
