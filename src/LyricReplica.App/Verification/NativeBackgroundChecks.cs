using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricReplica.Core;

namespace LyricReplica.App;

public sealed partial class MainWindow
{
    private async Task CheckNativeBackgroundsAsync(string folder)
    {
        File.Delete(Path.Combine(folder, "native-backgrounds.json"));
        var results = new List<object>();
        _options.ReduceMotion = false;
        Width = 1240;
        Height = 820;
        foreach (var mode in new[] { BackgroundMode.Isolation, BackgroundMode.Modern })
        {
            _options.Background = mode;
            await _background.SetModeAsync(mode, _options, true);
            _rebuildPending = true;
            await WaitForRenderer();
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
        WindowState = WindowState.Maximized;
        await _background.SetModeAsync(BackgroundMode.Modern, _options, true);
        _rebuildPending = true;
        await WaitForRenderer();
        if (!_background.IndependentRendererActive || ResizeMode != ResizeMode.NoResize)
            throw new InvalidOperationException("The independent background host did not enter the native NoResize path.");
        var target = ((HwndSource)PresentationSource.FromVisual(this)).CompositionTarget;
        var foreground = new RenderTargetBitmap(8, 8, 96, 96, PixelFormats.Pbgra32);
        foreground.Render(_root);
        var pixel = new byte[4];
        foreground.CopyPixels(new Int32Rect(1, 1, 1, 1), pixel, 4, 0);
        if (!_independentPresentation || ((SolidColorBrush)Background).Color.A != 0 || ((SolidColorBrush)_background.Background).Color.A != 0 || target.BackgroundColor.A != 0 || pixel[3] != 0)
            throw new InvalidOperationException($"Independent foreground state: enabled={_independentPresentation}, window={Background}, control={_background.Background}, target={target.BackgroundColor}, pixelAlpha={pixel[3]}, frames={_frameCount}.");
        results.Add(new
        {
            mode = "independent",
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
        WindowState = WindowState.Normal;
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
        async Task WaitForRenderer()
        {
            await Task.Delay(200);
            for (int i = 0; i < 100 && (!_background.RendererReady || _background.IndependentRendererActive && !_independentPresentation) && _background.LastError is null; i++)
                await Task.Delay(50);
            if (_background.LastError is { } error)
                throw new InvalidOperationException(error);
            if (!_background.RendererReady)
                throw new InvalidOperationException("Native background did not present its first frame within 5 seconds.");
        }
    }
}
