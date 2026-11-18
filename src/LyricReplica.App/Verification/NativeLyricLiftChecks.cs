using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricReplica.Core;

namespace LyricReplica.App;

public sealed partial class MainWindow
{
    internal async Task VerifyWindowedLyricLiftAsync()
    {
        await SeedDemoTracksForVerificationAsync();
        Width = 900;
        Height = 650;
        _options.Float = true;
        _options.AppleMusicLyrics.KaraokeStyleEnableAnimation = true;
        _options.AppleMusicLyrics.KaraokeStyleFloatHeight = 8;
        ShowLyrics();
        _options.ReduceMotion = false;
        await Task.Delay(400);

        var word = WordEdgeChecks.Find(_rows[0].Main).OfType<TextBlock>()
            .First(text => text.Text.Any(Timeline.IsHan) && text.RenderTransform is TranslateTransform or TransformGroup);
        int automaticPixels = FractionalImages(word, 0);
        int cachedPixels = FractionalImages(word, 1.5);
        var poses = new HashSet<double>();
        List<double> work = [], intervals = [];
        TimeSpan? previousFrame = null;
        int repeatedFrames = 0;
        _clock.Seek(6800);
        _clock.Play();
        CompositionTarget.Rendering -= OnRendering;
        CompositionTarget.Rendering += Sample;
        try { await Task.Delay(2200); }
        finally
        {
            CompositionTarget.Rendering -= Sample;
            CompositionTarget.Rendering += OnRendering;
            _clock.Pause();
        }
        string folder = Path.Combine(App.DataFolder, "verification");
        Directory.CreateDirectory(folder);
        double? liveRenderAtScale = (word.CacheMode as BitmapCache)?.RenderAtScale;
        bool passed = work.Count >= 10 && poses.Count > 4 && liveRenderAtScale == 1.5 && cachedPixels > automaticPixels;
        File.WriteAllText(Path.Combine(folder, "windowed-lyric-lift.json"), JsonSerializer.Serialize(new
        {
            passed, assemblySha256 = App.AssemblySha256,
            window = new { ActualWidth, ActualHeight, WindowState }, frames = work.Count, repeatedFrames,
            distinctLiftPositions = poses.Count, automaticPixels, cachedPixels, liveRenderAtScale,
            medianFrameIntervalMs = Percentile(intervals, .5), p95FrameIntervalMs = Percentile(intervals, .95),
            p95UpdateMs = Percentile(work, .95), maxUpdateMs = work.DefaultIfEmpty().Max()
        }, LyricsFile.JsonOptions));
        if (!passed)
            throw new InvalidOperationException("Windowed playback must render the active word lift with fractional bitmap filtering.");

        void Sample(object? sender, EventArgs e)
        {
            if (e is not RenderingEventArgs frame)
                return;
            if (previousFrame == frame.RenderingTime)
                repeatedFrames++;
            else if (previousFrame is { } previous)
                intervals.Add((frame.RenderingTime - previous).TotalMilliseconds);
            previousFrame = frame.RenderingTime;
            long start = Stopwatch.GetTimestamp();
            OnRendering(sender, e);
            work.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            poses.Add(word.RenderTransform.Value.OffsetY);
        }
    }

    private static double Percentile(List<double> values, double rank)
    {
        if (values.Count == 0)
            return 0;
        values.Sort();
        return values[(int)Math.Round((values.Count - 1) * rank)];
    }

    private static int FractionalImages(TextBlock original, double renderAtScale)
    {
        var offset = new TranslateTransform(6, 8);
        var text = new TextBlock
        {
            Text = original.Text, FontFamily = original.FontFamily, FontSize = original.FontSize, FontWeight = original.FontWeight,
            Foreground = Brushes.White, SnapsToDevicePixels = original.SnapsToDevicePixels, RenderTransform = offset
        };
        TextOptions.SetTextFormattingMode(text, TextOptions.GetTextFormattingMode(original));
        if (renderAtScale > 0)
            text.CacheMode = new BitmapCache { SnapsToDevicePixels = false, RenderAtScale = renderAtScale };
        var surface = new Canvas { Width = 240, Height = 100 };
        surface.Children.Add(text);
        surface.Measure(new Size(240, 100));
        surface.Arrange(new Rect(0, 0, 240, 100));
        var images = new HashSet<uint>();
        byte[] pixels = new byte[240 * 100 * 4];
        for (int i = 0; i <= 12; i++)
        {
            offset.Y = 8 - i * .12;
            var bitmap = new RenderTargetBitmap(240, 100, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            bitmap.CopyPixels(pixels, 240 * 4, 0);
            uint hash = 2166136261;
            foreach (byte pixel in pixels)
                hash = (hash ^ pixel) * 16777619;
            images.Add(hash);
        }
        return images.Count;
    }
}
