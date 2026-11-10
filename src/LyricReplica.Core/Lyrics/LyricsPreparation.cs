using System.Text.RegularExpressions;

namespace LyricReplica.Core;

public static class LyricsPreparation
{
    public const double TimelineCompensationMs = 300;
    private static readonly Regex CjkPunctuation = new("""[`~!@#$%^&*()+=|{}':;',\[\].<>/?~！@#￥%……&*（）——+|{}《》【】‘；："”“’。，、？]""");
    private static readonly Regex CjkPattern = new(@"\p{IsCJKUnifiedIdeographs}|^[\u0800-\u4e00]|^[\u4e00-\u9fa5]|^[\uac00-\ud7ff]");
    public static bool IsCjkLyrics(IReadOnlyList<LyricLine> lines) => lines.Count > 0 && lines.Count(line => CjkPattern.IsMatch(CjkPunctuation.Replace(line.Text, ""))) / (double)lines.Count > .66;
    public static bool UseKaraoke(Song song, AppleMusicSettings settings) => !song.Untimed && settings.Karaoke && song.Lines.Any(line => line.Words.Count > 0) && (!settings.IsCjkKaraokeDisabled || !IsCjkLyrics(song.Lines));
    private sealed class Entry(LyricLine source)
    {
        public LyricLine Source { get; } = source;
        public double Start = source.StartMs, End = source.EffectiveEndMs;
        public bool? Background = source.Background;
        public bool IsBackground => Background ?? Source.IsBackground;
        public bool Bracketed => Source.Words.Count > 0 && Source.Text.Length > 1 && ((Source.Text[0] == '(' && Source.Text[^1] == ')') || (Source.Text[0] == '（' && Source.Text[^1] == '）'));
        public double TrueStart => Source.Words.FirstOrDefault()?.StartMs ?? Source.StartMs;
    }

    public static List<DisplayLine> ForPage(Song song, bool duet, bool rightFirst, AppleMusicSettings? settings = null, bool enableMultiLine = true)
    {
        if (song.Untimed)
            return Timeline.Group(song, duet, rightFirst);
        var entries = song.Lines.Where(line => line.Text.Length > 0).Select(line => new Entry(line)).ToList();
        if (entries.Count == 0)
            return [];
        OptimizeBackground(entries);
        if ((settings is null || UseKaraoke(song, settings)) && entries.Any(row => row.Source.Words.Count > 0))
            PrepareSing(entries, settings?.InterLineAnimationPreset != InterLineAnimationPresetTypes.Lyricify, enableMultiLine);
        else
        for (int i = 0; i < entries.Count - 1; i++)
        {
            double gap = entries[i + 1].Start - entries[i].End;
            if (gap < 2000 && (gap > -323 || !enableMultiLine))
                entries[i].End = entries[i + 1].Start;
        }
        bool twoSided = song.Lines.Any(line => line.AlignLeft == false);
        var result = new List<DisplayLine>();
        for (int i = 0; i < entries.Count; i++)
        {
            var row = entries[i];
            IReadOnlyList<LyricLine> children = [];
            if (i + 1 < entries.Count && entries[i + 1].IsBackground)
                children = [BackgroundText.ForDisplay(entries[++i].Source, row.Source.AlignLeft)];
            var displayed = settings?.ShinyCjk == true ? ShinyWords.ForDisplay(row.Source) : row.Source;
            result.Add(new DisplayLine(displayed, children, row.Start, duet && (twoSided ? row.Source.AlignLeft == false : rightFirst), false, row.End));
        }
        return result;
    }

    private static void PrepareSing(List<Entry> entries, bool ios, bool multiLine)
    {
        // Keep the reference compensation fixed; the host applies the user's lyric lead to its clock.
        const double timelineOffset = TimelineCompensationMs;
        var main = new List<Entry>();
        var starts = new Dictionary<Entry, double>();
        for (int i = 0; i < entries.Count; i++)
        {
            var row = entries[i];
            main.Add(row);
            starts[row] = row.TrueStart;
            if (i + 1 < entries.Count && entries[i + 1].IsBackground)
            {
                var child = entries[++i];
                if (ios)
                {
                    starts[row] = Math.Min(row.TrueStart, child.TrueStart);
                    row.Start = Math.Min(row.Start, child.Start);
                }
            }
        }
        main.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (int i = 0; i < main.Count - 1; i++)
        {
            var current = main[i];
            var next = main[i + 1];
            double gap = next.Start - current.End;
            bool breathing = starts[next] - current.End > 6700;
            double start = starts[next] - Lead(breathing ? 0 : (starts[next] - Math.Max(current.Source.EffectiveEndMs, current.End)) / 1000) + timelineOffset;
            if (gap > 6700)
            {
                if (i > 0 && current.Start - main[i - 1].End < 0)
                    main[i - 1].End += 300;
                current.End += 300;
                next.Start = ios ? start : next.Start - 300;
            }
            else if (gap < -323 && multiLine)
            {
                if (i < main.Count - 2 && Math.Max(current.End, next.End) < main[i + 2].Start)
                {
                    current.End = Math.Max(current.End, next.End);
                    next.End = current.End;
                }
                else
                    current.End = next.End;
            }
            else
            {
                double boundary = ios ? start : gap > 1300 ? next.Start - 1000 : gap > 450 ? current.End + 50 : gap > 300 ? current.End + 300 : Math.Min(current.End, next.Start);
                if (i > 0 && main[i - 1].End == current.End)
                    main[i - 1].End = boundary;
                current.End = next.Start = boundary;
            }
        }
        var last = main[^1];
        if (last.End != 0)
        {
            if (main.Count >= 2 && main[^2].End == last.End)
                main[^2].End += 300;
            last.End += 300;
        }
        main[0].Start = ios ? Math.Max(starts[main[0]] - Lead(0) + timelineOffset, 0) : Math.Max(main[0].Start - 300, 0);
        for (int i = 0; i < main.Count - 2; i++)
        if (main[i].End > main[i + 2].Start)
            main[i].End = main[i + 2].Start;
    }

    private static int Lead(double gap) => Math.Max(1, (int)Math.Round(Motion.ScrollParameters(gap).Duration * 1000) - 100);

    private static void OptimizeBackground(List<Entry> lines)
    {
        bool explicitFlags = lines.Any(line => line.Background.HasValue);
        for (int i = 0; i < lines.Count; i++)
        {
            var row = lines[i];
            if (i + 1 >= lines.Count || !lines[i + 1].IsBackground)
            {
                if (!explicitFlags && row.IsBackground && i > 0 && lines[i - 1].Bracketed)
                    row.Background = false;
                continue;
            }
            var child = lines[i + 1];
            if (!explicitFlags && row.IsBackground)
            {
                row.Background = child.Background = false;
                i++;
                continue;
            }
            if (i < lines.Count - 2)
            {
                var next = lines[i + 2];
                if (!explicitFlags && next.IsBackground)
                {
                    child.Background = next.Background = false;
                    i += 2;
                    continue;
                }
                double end = Math.Max(child.End, row.End);
                if (next.Start >= end || i >= lines.Count - 3 || !lines[i + 3].IsBackground)
                    row.End = end;
                else if (i < lines.Count - 4 && lines[i + 4].Start < Math.Max(end, Math.Max(lines[i + 3].End, next.End)))
                {
                    if (row.End < next.Start)
                        row.End = next.Start - 100;
                }
                else
                    row.End = end;
            }
            else
                row.End = Math.Max(child.End, row.End);
            if (i > 3 && child.Start < row.Start)
            {
                double priorEnd = lines[i - 1].IsBackground ? (lines[i - 3].IsBackground ? lines[i - 4].End : lines[i - 3].End) : (lines[i - 2].IsBackground ? lines[i - 3].End : lines[i - 2].End);
                var previous = lines[lines[i - 1].IsBackground ? i - 2 : i - 1];
                if (i >= lines.Count - 3 && priorEnd > previous.Start && Math.Min(child.Start, row.Start) < previous.End)
                {
                    if (child.Start < previous.End && row.Start >= previous.End)
                        row.Start = previous.End;
                }
                else
                    row.Start = Math.Min(child.Start, row.Start);
            }
            else
                row.Start = Math.Min(child.Start, row.Start);
            i++;
        }
    }
}
