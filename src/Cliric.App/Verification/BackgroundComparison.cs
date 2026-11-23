using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lyricify.Backgrounds;
using Cliric.Core;

namespace Cliric.App;

public sealed partial class MainWindow
{
    private async Task CompareIosBackgroundsAsync(string folder)
    {
        if (!ReferenceAppleMusicBackground.Available)
            throw new InvalidOperationException("The local 4.4.2 background components are required for this comparison.");

        const BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        var results = new List<object>();
        foreach (var (name, width, height, slot, light) in new[] { ("landscape", 420, 270, 0, false), ("portrait", 300, 440, 2, true) })
        {
            var settings = BackgroundView.GetIosSettings(new ViewOptions());
            settings.BassPulseScale = 0;
            using var original = BackgroundView.CreateIosSession(BackgroundMode.ReferenceAppleMusic, settings, light, independent: false, slot);
            using var modern = BackgroundView.CreateIosSession(BackgroundMode.Modern, settings, light, independent: false, slot);
            if (original.GetType().Assembly == modern.GetType().Assembly)
                throw new InvalidOperationException("The A/B renderers must come from independent assemblies.");

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var originalView = (FrameworkElement)original;
            var modernView = (FrameworkElement)modern;
            Attach(originalView);
            Attach(modernView);
            var window = new Window { Owner = this, Content = panel, ShowActivated = false, ShowInTaskbar = false,
                SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Title = "背景渲染对照" };
            try
            {
                window.Show();
                var state = new BackgroundState { IsPlaying = false, IsVertical = height > width, IsVisible = true, IsBehindLyrics = true, IsLightTheme = light };
                original.UpdateState(state);
                modern.UpdateState(state);
                var artwork = new BackgroundArtwork("compare-" + name, Artwork.Encode(Artwork.Create(slot)));
                await Task.WhenAll(original.SetArtworkAsync(artwork), modern.SetArtworkAsync(artwork));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var a = Renderer(original);
                var b = Renderer(modern);
                // The 4.4.2 normal D3DImage branch renders but does not announce FirstFramePresented.
                var originalSurface = a.View.GetType().GetField("_outputSurface", NonPublic)!;
                while (originalSurface.GetValue(a.View) is null || !modern.IsReady)
                    await Task.Delay(25, timeout.Token);
                a.Clock.Reset();
                b.Clock.Reset();
                CapturePair("start");
                a.Clock.Start();
                b.Clock.Start();
                await Task.Delay(1200);
                a.Clock.Stop();
                b.Clock.Stop();
                CapturePair("motion");

                void CapturePair(string phase)
                {
                    a.Frame.Invoke(a.View, null);
                    b.Frame.Invoke(b.View, null);
                    var combined = Snapshot(panel, 2 * width, height);
                    var first = new CroppedBitmap(combined, new Int32Rect(0, 0, width, height));
                    var second = new CroppedBitmap(combined, new Int32Rect(width, 0, width, height));
                    string image = $"background-ab-{name}-{phase}.png";
                    SavePair(first, second, image);
                    results.Add(new { layout = name, phase, preset = slot, light, width, height,
                        timeOriginalMs = a.Clock.Elapsed.TotalMilliseconds, timeModernMs = b.Clock.Elapsed.TotalMilliseconds,
                        dpi = VisualTreeHelper.GetDpi(originalView).PixelsPerInchX, image, difference = ComparePixels(first, second) });
                }
            }
            finally { window.Close(); }

            void Attach(FrameworkElement view)
            {
                view.Width = width;
                view.Height = height;
                panel.Children.Add(view);
            }
        }
        File.WriteAllText(Path.Combine(folder, "background-comparison.json"), JsonSerializer.Serialize(new
        {
            assemblySha256 = App.AssemblySha256,
            original = "WPF renderer and mesh assemblies bundled with Lyricify 4.4.2, shared host contracts and audio helper",
            modern = "Current build of the open-source WPF renderer",
            scope = "Same covers, dimensions, DPI and preset; normal WPF window with system-audio response disabled",
            results
        }, LyricsFile.JsonOptions));

        static (object View, Stopwatch Clock, MethodInfo Frame) Renderer(IBackgroundSession session)
        {
            object renderer = session.GetType().GetField("renderer", NonPublic)!.GetValue(session)!;
            var type = renderer.GetType();
            return (renderer, (Stopwatch)type.GetField("_animationClock", NonPublic)!.GetValue(renderer)!, type.GetMethod("RenderFrame", NonPublic)!);
        }

        void SavePair(BitmapSource first, BitmapSource second, string image)
        {
            const int labelHeight = 32;
            const int gap = 12;
            int width = first.PixelWidth;
            int height = first.PixelHeight;
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 2 * width + gap, height + labelHeight));
                drawing.DrawText(Label("Apple Music 原版 · 4.4.2"), new Point(8, 7));
                drawing.DrawText(Label("Lyricify 现代 · 开源"), new Point(width + gap + 8, 7));
                drawing.DrawImage(first, new Rect(0, labelHeight, width, height));
                drawing.DrawImage(second, new Rect(width + gap, labelHeight, width, height));
            }
            var pair = Snapshot(visual, 2 * width + gap, height + labelHeight);
            File.WriteAllBytes(Path.Combine(folder, image), Artwork.Encode(pair));
        }

        static FormattedText Label(string text) => new(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), 15, Brushes.White, 1);
    }

    private static RenderTargetBitmap Snapshot(Visual view, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        return bitmap;
    }

    private static object ComparePixels(BitmapSource first, BitmapSource second)
    {
        int stride = 4 * first.PixelWidth;
        var a = new byte[stride * first.PixelHeight];
        var b = new byte[a.Length];
        first.CopyPixels(a, stride, 0);
        second.CopyPixels(b, stride, 0);
        long total = 0;
        int max = 0, matchingPixels = 0;
        int rangeA = 0, rangeB = 0, minA = 255, minB = 255;
        for (int i = 0; i < a.Length; i += 4)
        {
            int pixelMax = 0;
            for (int channel = 0; channel < 3; channel++)
            {
                int x = a[i + channel], y = b[i + channel];
                int difference = Math.Abs(x - y);
                total += difference;
                pixelMax = Math.Max(pixelMax, difference);
                rangeA = Math.Max(rangeA, x);
                rangeB = Math.Max(rangeB, y);
                minA = Math.Min(minA, x);
                minB = Math.Min(minB, y);
            }
            max = Math.Max(max, pixelMax);
            if (pixelMax <= 2)
                matchingPixels++;
        }
        if (rangeA - minA < 10 || rangeB - minB < 10)
            throw new InvalidOperationException("The capture did not include both rendered backgrounds.");
        int count = a.Length / 4;
        return new { rgbMeanError = total / (3.0 * count), largestChannelError = max, pixelsWithin2 = 100.0 * matchingPixels / count };
    }
}
