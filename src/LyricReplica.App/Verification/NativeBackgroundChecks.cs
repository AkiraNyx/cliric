using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
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

        ShowLyrics();
        await Task.Delay(100);
        double progress = LyricsProgress;
        if (progress is <= .02 or >= .99 ||
            _library!.Effect is not BlurEffect { Radius: > 0 } || _root.Effect is not BlurEffect { Radius: > 0 } ||
            Math.Abs(_library.Opacity + _root.Opacity - 1) > .001 ||
            _backdropNeutral.Opacity is <= 0 or >= .58 || _backdropShade.Opacity is <= 0 or >= .18 ||
            _legacyTime <= time || crop != BackdropBounds() || viewport != _shell.RenderSize ||
            !ReferenceEquals(chrome, WindowChrome.GetWindowChrome(this)))
            throw new InvalidOperationException($"The page blend must advance both Gaussian fades and the same backdrop in a fixed viewport. Progress={progress:F3}.");
        CaptureShell(Path.Combine(folder, "lyrics-transition.png"));
        progress = LyricsProgress;
        ShowLibrary();
        if (Math.Abs(progress - LyricsProgress) > .025)
            throw new InvalidOperationException("Reversing the page blend must continue from its current progress.");
        await Task.Delay(300);
        if (LyricsProgress != 0 || _root.IsVisible || !_library.IsVisible || _library.Effect is not null)
            throw new InvalidOperationException("Returning must settle on the sharp library surface.");
        CaptureShell(Path.Combine(folder, "player-shared-background.png"));

        ShowLyrics();
        await Task.Delay(300);
        if (LyricsProgress != 1 || !_root.IsVisible || _library.IsVisible || _root.Effect is not null ||
            _backdropNeutral.Opacity != 0 || _backdropShade.Opacity != 0 || viewport != _shell.RenderSize)
            throw new InvalidOperationException("The settled lyric page must show the unfiltered shared backdrop.");
        CaptureShell(Path.Combine(folder, "lyrics-shared-background.png"));
        ShowLibrary();
        await Task.Delay(300);

        Rect BackdropBounds() => backdrop.TransformToVisual(_shell).TransformBounds(new Rect(backdrop.RenderSize));
    }

    private async Task CheckNativeBackgroundsAsync(string folder)
    {
        File.Delete(Path.Combine(folder, "native-backgrounds.json"));
        var results = new List<object>();
        _options.ReduceMotion = false;
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
            WindowState = WindowState.Maximized;
            await WaitForRenderer();
            var renderer = _background.Children[0];
            var size = _shell.RenderSize;
            ShowLibrary();
            await Task.Delay(300);
            if (!_independentPresentation || !_library!.IsVisible || WindowState != WindowState.Maximized ||
                !ReferenceEquals(renderer, _background.Children[0]) || _shell.RenderSize != size)
                throw new InvalidOperationException("Returning while maximized must preserve the same native background and viewport.");
            ShowLyrics();
            await Task.Delay(300);
            if (!_independentPresentation || !_root.IsVisible || WindowState != WindowState.Maximized ||
                !ReferenceEquals(renderer, _background.Children[0]) || _shell.RenderSize != size)
                throw new InvalidOperationException("Opening lyrics while maximized must preserve the same native background and viewport.");
            WindowState = WindowState.Normal;
        }
        ToggleFullscreen();
        await _background.SetModeAsync(_options.Background, _options, true);
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
