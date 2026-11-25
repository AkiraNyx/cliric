using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using Cliric.Core;

namespace Cliric.App;

public sealed partial class MainWindow
{
    private readonly record struct SwitchFrameReport(int Frames, int Switches, bool Independent,
        double Fps, double P95FrameMs, double P95WorkMs, double P95SwitchMs,
        double StartMs, double EndMs, double MaxSwitchMs, bool IndependentPresented, bool BackgroundFaulted, bool PlusLighterActive);

    internal async Task VerifyLyricSwitchFramesAsync()
    {
        bool liked = _args.Contains("--lyric-switch-liked");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var token = timeout.Token;
        Width = 1240;
        Height = 820;
        bool reference = ReferenceAppleMusicBackground.Available;
        try
        {
            if (liked)
            {
                var page = await NetEaseMusic.ReadClientLibraryAsync("liked", 0, 0, token);
                if (page.TrackIds is not { Length: > 0 } ids)
                    throw new InvalidOperationException("我喜欢的音乐没有返回首曲。");
                var first = page.Tracks.FirstOrDefault(track => track.OriginalId == ids[0])
                    ?? throw new InvalidOperationException("未能读取我喜欢的音乐的首曲资料。");
                if (!await PlayNetEaseAsync(first, token))
                    throw new InvalidOperationException("我喜欢的音乐的首曲未开始播放。");
            }
            else
            {
                _options.ReduceMotion = false;
                _options.AudioReactive = false;
                _options.EnableMultiLine = false;
                _options.Float = true;
                _options.AppleMusicLyrics.Karaoke = true;
                _options.AppleMusicLyrics.PullingEffect = true;
                _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer = true;
                _options.Background = reference ? BackgroundMode.ReferenceAppleMusic : BackgroundMode.Modern;
                _songs.Add(CreateLyricFrameFixture());
                await LoadSongAsync(_songs.Count - 1);
            }

            ShowLyrics();
            var timed = _groups.Where(line => !line.Untimed && line.EndMs > line.StartMs).ToArray();
            if (timed.Length < 4)
                throw new InvalidOperationException("曲目需要至少四句有时间轴的歌词。");
            double start = liked ? Math.Max(0, timed[0].StartMs - _options.TimelineOffsetMs - 400) : 900;
            double duration = liked ? Math.Clamp(timed[Math.Min(4, timed.Length - 1)].StartMs - _options.TimelineOffsetMs - start + 800, 7500, 22000) : 6100;
            var windowed = await MeasureAsync(start, duration);
            if (windowed.Independent || windowed.IndependentPresented)
                throw new InvalidOperationException("Windowed lyrics must retain the inline background.");
            ToggleFullscreen();
            var fullscreen = await MeasureAsync(start, duration);
            if (windowed.Switches < 2 || fullscreen.Switches < 2)
                throw new InvalidOperationException("Each window mode must include at least two line changes.");
            File.WriteAllText(Path.Combine(App.DataFolder, "lyric-switch-frames.json"),
                JsonSerializer.Serialize(new
                {
                    reference, liked, track = liked ? _song.Title : null, artist = liked ? _song.Artist : null,
                    background = _options.Background.ToString(), _options.AudioReactive, _options.ReduceMotion, _options.AppleMusicLyrics.EnablePlusLighter,
                    windowed, fullscreen
                }, LyricsFile.JsonOptions));
        }
        finally
        {
            if (liked)
                await StopNetEaseAsync();
        }

        async Task<SwitchFrameReport> MeasureAsync(double start, double duration)
        {
            if (liked)
            {
                if (_netEasePlayRequested)
                    await ControlNetEaseAsync("pause", true);
                await ControlNetEaseAsync("time-pos", start / 1000);
            }
            else
                _clock.Seek(start);
            await Task.Delay(1000, token);
            List<double> frameMs = [], workMs = [], switchMs = [];
            long previousTick = 0;
            TimeSpan previousFrame = TimeSpan.MinValue;
            int switches = 0, switchFrames = 0;
            if (liked)
            {
                await ControlNetEaseAsync("pause", false);
                for (int i = 0; i < 20 && !_clock.IsPlaying; i++)
                    await Task.Delay(50, token);
                if (!_clock.IsPlaying || Math.Abs(_clock.PositionMs - start) > 1500)
                    throw new InvalidOperationException("首曲未在请求的测试位置开始播放。");
            }
            else
                _clock.Play();
            double startedAt = _clock.PositionMs, endedAt = startedAt;
            CompositionTarget.Rendering -= OnRendering;
            CompositionTarget.Rendering += Sample;
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(duration), token);
                endedAt = _clock.PositionMs;
            }
            finally
            {
                CompositionTarget.Rendering -= Sample;
                CompositionTarget.Rendering += OnRendering;
                if (liked && _netEasePlayRequested)
                    await ControlNetEaseAsync("pause", true);
                else
                    _clock.Pause();
            }
            return new(frameMs.Count, switches, _background.IndependentRendererActive,
                frameMs.Count > 0 ? 1000 / frameMs.Average() : 0,
                Percentile(frameMs, .95), Percentile(workMs, .95), Percentile(switchMs, .95),
                startedAt, endedAt, switchMs.DefaultIfEmpty().Max(), _independentPresentation, _background.LastError is not null, _blend.Active);

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

    private static Song CreateLyricFrameFixture()
    {
        const int lineLength = 1350;
        const string text = "这一行窗边的光慢慢向下一行移动";
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
        return song;
    }
}
