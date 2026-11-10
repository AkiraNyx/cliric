using System.Text.Json.Serialization;

namespace LyricReplica.Core;

public sealed class Song
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "未命名歌曲";
    public string Artist { get; set; } = "未知艺人";
    public string Album { get; set; } = "";
    public string? AlbumId
    {
        get; set;
    }
    public string Credit { get; set; } = "";
    public string? AudioPath
    {
        get; set;
    }
    public string? CoverPath
    {
        get; set;
    }
    public string? ArtworkVideoPath
    {
        get; set;
    }
    public double DurationMs
    {
        get; set;
    }
    public bool Untimed
    {
        get; set;
    }
    public List<LyricLine> Lines { get; set; } = [];

    public void Validate()
    {
        if (Lines is null)
            throw new FormatException("歌词列表为空。");
        CheckTime(DurationMs);
        if (Lines.Count > 5000)
            throw new FormatException("歌词超过 5000 行。");
        foreach (var line in Lines)
        {
            if (line is null || line.Words is null)
                throw new FormatException("歌词行或逐字列表无效。");
            line.Text ??= "";
            line.Translation ??= "";
            line.Romanization ??= "";
            if (line.Text.Length > 16384 || line.Translation.Length > 16384 || line.Romanization.Length > 16384 || line.Words.Count > 2000)
                throw new FormatException("歌词行过长。");
            CheckTime(line.StartMs);
            CheckTime(line.EndMs);
            foreach (var word in line.Words)
            {
                if (word is null || word.Syllables is null)
                    throw new FormatException("逐字或音节列表无效。");
                word.Text ??= "";
                CheckTime(word.StartMs);
                CheckTime(word.DurationMs);
                CheckTime(word.EndMs);
                if (word.Syllables.Count > 2000 || word.Text.Length > 16384)
                    throw new FormatException("逐字数据过长。");
                foreach (var syllable in word.Syllables)
                {
                    if (syllable is null)
                        throw new FormatException("音节无效。");
                    syllable.Text ??= "";
                    CheckTime(syllable.StartMs);
                    CheckTime(syllable.DurationMs);
                    CheckTime(syllable.EndMs);
                }
                word.Syllables = word.Syllables.OrderBy(x => x.StartMs).ToList();
            }
            line.Words = line.Words.OrderBy(x => x.StartMs).ToList();
            if (line.EndMs != 0 && line.EndMs < line.StartMs)
                throw new FormatException("歌词结束早于开始。");
        }
        if (DurationMs == 0)
            DurationMs = Math.Min(86_400_000, Math.Max(1000, Lines.Select(x => x.EffectiveEndMs).DefaultIfEmpty(0).Max() + 3000));
    }

    private static void CheckTime(double time)
    {
        if (!double.IsFinite(time) || time < 0 || time > 86_400_000)
            throw new FormatException("歌词时间必须在 0 到 24 小时之间。");
    }
}

public sealed class LyricLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public string Translation { get; set; } = "";
    public string Romanization { get; set; } = "";
    public double StartMs
    {
        get; set;
    }
    public double EndMs
    {
        get; set;
    }
    public bool? Background
    {
        get; set;
    }
    public bool? AlignLeft
    {
        get; set;
    }
    public List<LyricWord> Words { get; set; } = [];
    [JsonIgnore] public double EffectiveEndMs => Words.Count > 0 ? Words.Max(x => x.EndMs) : EndMs;
    [JsonIgnore] public bool IsBackground => Background ?? (Words.Count > 0 && IsBracketed(Text));
    private static bool IsBracketed(string text)
    {
        var value = text;
        return value.Length > 1 && ((value.StartsWith('(') && value.EndsWith(')')) || (value.StartsWith('（') && value.EndsWith('）')));
    }
}

public sealed class LyricWord
{
    public string Text { get; set; } = "";
    public double StartMs
    {
        get; set;
    }
    public double DurationMs
    {
        get; set;
    }
    public List<Syllable> Syllables { get; set; } = [];
    [JsonIgnore] public double EndMs => StartMs + DurationMs;
}

public sealed class Syllable
{
    public string Text { get; set; } = "";
    public double StartMs
    {
        get; set;
    }
    public double DurationMs
    {
        get; set;
    }
    [JsonIgnore] public double EndMs => StartMs + DurationMs;
}

public sealed record DisplayLine(LyricLine Main, IReadOnlyList<LyricLine> Backgrounds, double StartMs, bool Right, bool Untimed = false, double? PreparedEndMs = null)
{
    public double EndMs => PreparedEndMs ?? Main.EffectiveEndMs;
}

public enum MotionPreset
{
    Ios264, Ios, Android, Lyricify
}
public enum BackgroundMode
{
    Modern, Classic, Legacy, Solid, Static, DynamicBeating, Isolation
}
public enum RomajiOptions
{
    Disabled, TranslationFirst, RomajiFirst, RomajiOnly
}

public sealed class ViewOptions
{
    public AppleMusicSettings AppleMusicLyrics { get; set; } = new();
    public bool Translation { get; set; } = true;
    public RomajiOptions RomajiOption
    {
        get; set;
    }
    [JsonIgnore]
    public bool Romanization
    {
        get => RomajiOption != RomajiOptions.Disabled; set => RomajiOption = value ? RomajiOptions.RomajiFirst : RomajiOptions.Disabled;
    }
    public bool EnableMultiLine { get; set; } = true;
    public bool EnableColorfulEmoji { get; set; } = true;
    [JsonIgnore]
    public bool Blur
    {
        get => AppleMusicLyrics.BlurLyrics; set => AppleMusicLyrics.BlurLyrics = value;
    }
    [JsonIgnore]
    public bool Bold
    {
        get => AppleMusicLyrics.BoldText; set => AppleMusicLyrics.BoldText = value;
    }
    [JsonIgnore]
    public double BlurLevel
    {
        get => AppleMusicLyrics.BlurLevel; set => AppleMusicLyrics.BlurLevel = value;
    }
    [JsonIgnore]
    public bool Duet
    {
        get => AppleMusicLyrics.DuetView; set => AppleMusicLyrics.DuetView = value;
    }
    [JsonIgnore]
    public bool RightAlignFirst
    {
        get => AppleMusicLyrics.RightAlignFirst; set => AppleMusicLyrics.RightAlignFirst = value;
    }
    [JsonIgnore]
    public bool WordShine
    {
        get => AppleMusicLyrics.KaraokeStyleWordShine; set => AppleMusicLyrics.KaraokeStyleWordShine = value;
    }
    [JsonIgnore]
    public bool Float
    {
        get => AppleMusicLyrics.KaraokeStyleFloat; set => AppleMusicLyrics.KaraokeStyleFloat = value;
    }
    public bool ReduceMotion
    {
        get; set;
    }
    public bool LightTheme
    {
        get; set;
    }
    [JsonIgnore]
    public bool ShowFps
    {
        get => AppleMusicLyrics.ShowFpsOverlay; set => AppleMusicLyrics.ShowFpsOverlay = value;
    }
    public bool MagicStrip { get; set; } = true;
    public bool Topmost
    {
        get; set;
    }
    public bool RealtimeSeek
    {
        get; set;
    }
    [JsonIgnore]
    public bool AnimatedTime
    {
        get => AppleMusicLyrics.EnableAnimatedTimeStatus; set => AppleMusicLyrics.EnableAnimatedTimeStatus = value;
    }
    public bool AudioReactive
    {
        get; set;
    }
    public double Zoom { get; set; } = 1;
    public double TimelineOffsetMs { get; set; } = 300;
    [JsonIgnore]
    public double AnimationSpeed
    {
        get => AppleMusicLyrics.AnimationSpeed; set => AppleMusicLyrics.AnimationSpeed = value;
    }
    public double AutoResumeSeconds { get; set; } = 10;
    public double Volume { get; set; } = .7;
    [JsonIgnore]
    public MotionPreset Preset
    {
        get => AppleMusicLyrics.WordShineEffectPreset switch { WordShineEffectPresetTypes.iOS264 => MotionPreset.Ios264, WordShineEffectPresetTypes.Android => MotionPreset.Android, WordShineEffectPresetTypes.Lyricify => MotionPreset.Lyricify, _ => MotionPreset.Ios };
        set => AppleMusicLyrics.WordShineEffectPreset = value switch { MotionPreset.Ios264 => WordShineEffectPresetTypes.iOS264, MotionPreset.Android => WordShineEffectPresetTypes.Android, MotionPreset.Lyricify => WordShineEffectPresetTypes.Lyricify, _ => WordShineEffectPresetTypes.iOS162 };
    }
    [JsonIgnore]
    public BackgroundMode Background
    {
        get => AppleMusicLyrics.Background switch { BackgroundTypes.Dynamic => BackgroundMode.Legacy, BackgroundTypes.DynamicBeating => BackgroundMode.DynamicBeating, BackgroundTypes.DynamicIsolation => BackgroundMode.Isolation, BackgroundTypes.AppleMusicOriginal => BackgroundMode.Modern, _ => BackgroundMode.Static };
        set => AppleMusicLyrics.Background = value switch { BackgroundMode.Legacy => BackgroundTypes.Dynamic, BackgroundMode.DynamicBeating => BackgroundTypes.DynamicBeating, BackgroundMode.Isolation => BackgroundTypes.DynamicIsolation, BackgroundMode.Modern or BackgroundMode.Classic => BackgroundTypes.AppleMusicOriginal, _ => BackgroundTypes.Static };
    }
    [JsonIgnore]
    public string FontFamily
    {
        get => AppleMusicLyrics.FontPreset switch { FontPresets.Custom => AppleMusicLyrics.FontFamily, FontPresets.SFPro => "SF Pro Display, SF Pro Text, Segoe UI", FontPresets.SpotifyCircular => "Spotify Circular, CircularSp, Segoe UI", _ => "Spotify Mix, Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI" };
        set
        {
            AppleMusicLyrics.FontPreset = FontPresets.Custom;
            AppleMusicLyrics.FontFamily = value ?? "";
        }
    }

    public void Normalize()
    {
        AppleMusicLyrics ??= new();
        AppleMusicLyrics.BackgroundAppleMusicOriginal ??= new();
        AppleMusicLyrics.BuggyAppleOptions ??= new();
        AppleMusicLyrics.ZoomingLevels ??= new();
        AppleMusicLyrics.FontFamily ??= "";
        Zoom = double.IsFinite(Zoom) ? Math.Clamp(Zoom, .1, 8) : 1;
        TimelineOffsetMs = double.IsFinite(TimelineOffsetMs) ? Math.Clamp(TimelineOffsetMs, -5000, 5000) : 300;
        AnimationSpeed = double.IsFinite(AnimationSpeed) ? Math.Clamp(AnimationSpeed, .25, 3) : 1;
        AutoResumeSeconds = double.IsFinite(AutoResumeSeconds) ? Math.Clamp(AutoResumeSeconds, 1, 60) : 10;
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : .7;
        BlurLevel = double.IsFinite(BlurLevel) ? Math.Clamp(BlurLevel, 0, 2) : .6;
        if (!Enum.IsDefined(Preset))
            Preset = MotionPreset.Ios;
        if (!Enum.IsDefined(RomajiOption))
            RomajiOption = RomajiOptions.Disabled;
        AppleMusicLyrics.PullingDelay = double.IsFinite(AppleMusicLyrics.PullingDelay) ? Math.Clamp(AppleMusicLyrics.PullingDelay, 0, 100) : 2.5;
        if (!Enum.IsDefined(AppleMusicLyrics.Background))
            AppleMusicLyrics.Background = BackgroundTypes.Static;
        AppleMusicLyrics.FpsOverlayUpdateInterval = double.IsFinite(AppleMusicLyrics.FpsOverlayUpdateInterval) ? Math.Clamp(AppleMusicLyrics.FpsOverlayUpdateInterval, .1, 2) : .5;
    }
}
