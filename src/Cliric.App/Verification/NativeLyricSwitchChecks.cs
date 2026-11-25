using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using Cliric.Core;

namespace Cliric.App;

public sealed partial class MainWindow
{
    private readonly record struct SwitchFrameReport(int Frames, int Switches, bool Independent,
        double Fps, double P95FrameMs, double P95WorkMs, double P95SwitchMs);

    internal async Task VerifyLyricSwitchFramesAsync()
    {
        const int lineLength = 1350;
        const string text = "这一行窗边的光慢慢向下一行移动";
        Width = 1240;
        Height = 820;
        _options.ReduceMotion = false;
        _options.AudioReactive = false;
        _options.EnableMultiLine = false;
        _options.Float = true;
        _options.AppleMusicLyrics.Karaoke = true;
        _options.AppleMusicLyrics.PullingEffect = true;
        _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer = true;
        bool reference = ReferenceAppleMusicBackground.Available;
        _options.Background = reference ? BackgroundMode.ReferenceAppleMusic : BackgroundMode.Modern;

        var song = new Song { Title = "Line movement", DurationMs = lineLength * 75 };
        for (int line = 0; line < 75; line++)
        {
            double start = line * lineLength;
            song.Lines.Add(new LyricLine
            {
                Text = text, StartMs = start, EndMs = start + lineLength,
                Words = text.Select((character, i) => new LyricWord
                {
                    Text = character.ToString(),
                    StartMs = start + i * lineLength / (double)text.Length,
                    DurationMs = lineLength / (double)text.Length
                }).ToList()
            });
        }
        _songs.Add(song);
        await LoadSongAsync(_songs.Count - 1);
        ShowLyrics();
        var windowed = await MeasureAsync();
        ToggleFullscreen();
        var fullscreen = await MeasureAsync();
        if (windowed.Switches < 2 || fullscreen.Switches < 2)
            throw new InvalidOperationException("Each window mode must include at least two line changes.");
        File.WriteAllText(Path.Combine(App.DataFolder, "lyric-switch-frames.json"),
            JsonSerializer.Serialize(new { reference, windowed, fullscreen }, LyricsFile.JsonOptions));

        async Task<SwitchFrameReport> MeasureAsync()
        {
            _clock.Seek(900);
            await Task.Delay(1000);
            List<double> frameMs = [], workMs = [], switchMs = [];
            long previousTick = 0;
            TimeSpan previousFrame = TimeSpan.MinValue;
            int switches = 0, switchFrames = 0;
            _clock.Play();
            CompositionTarget.Rendering -= OnRendering;
            CompositionTarget.Rendering += Sample;
            try { await Task.Delay(6100); }
            finally
            {
                CompositionTarget.Rendering -= Sample;
                CompositionTarget.Rendering += OnRendering;
                _clock.Pause();
            }
            return new(frameMs.Count, switches, _background.IndependentRendererActive,
                frameMs.Count > 0 ? 1000 / frameMs.Average() : 0,
                Percentile(frameMs, .95), Percentile(workMs, .95), Percentile(switchMs, .95));

            void Sample(object? sender, EventArgs e)
            {
                if (e is not RenderingEventArgs frame || frame.RenderingTime == previousFrame)
                    return;
                previousFrame = frame.RenderingTime;
                int anchor = _anchor;
                long tick = Stopwatch.GetTimestamp();
                OnRendering(sender, e);
                workMs.Add(Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
                if (anchor >= 0 && _anchor != anchor)
                {
                    if (_rows.Any(row => row.Main.LinePlaybackActive && !row.Main.SlotOpen))
                        throw new InvalidOperationException("An outgoing row must leave its playing pose at the line change.");
                    switches++;
                    switchFrames = 4;
                }
                if (previousTick != 0)
                {
                    double elapsed = Stopwatch.GetElapsedTime(previousTick, tick).TotalMilliseconds;
                    frameMs.Add(elapsed);
                    if (switchFrames > 0)
                    {
                        switchMs.Add(elapsed);
                        switchFrames--;
                    }
                }
                previousTick = tick;
            }
        }
    }
}
