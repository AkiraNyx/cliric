namespace Cliric.Core;

public static class Timeline
{
    public static List<DisplayLine> Group(Song song, bool duet, bool rightFirst)
    {
        bool twoSided = song.Lines.Any(x => x.AlignLeft == false);
        var result = new List<DisplayLine>();
        foreach (var line in song.Lines)
        {
            if (line.IsBackground && result.Count > 0)
            {
                var previous = result[^1];
                var backgrounds = previous.Backgrounds.Append(BackgroundText.ForDisplay(line, previous.Main.AlignLeft)).OrderBy(x => x.StartMs).ToArray();
                result[^1] = previous with
                {
                    Backgrounds = backgrounds,
                    StartMs = Math.Min(previous.StartMs, line.StartMs)
                };
            }
            else
            {
                bool right = duet && (twoSided ? line.AlignLeft == false : rightFirst);
                result.Add(new DisplayLine(line, [], line.StartMs, right, song.Untimed));
            }
        }
        return result.OrderBy(x => x.StartMs).ToList();
    }

    public static int Anchor(IReadOnlyList<DisplayLine> lines, double time)
    {
        if (lines.Count == 0)
            return -1;
        if (lines[0].Untimed)
            return -1;
        int latest = -1;
        for (int i = lines.Count - 1; i >= 0; i--)
        if (lines[i].StartMs <= time)
        {
            latest = i;
            break;
        }
        if (latest < 0)
            return -1;
        while (latest > 0 && lines[latest - 1].EndMs >= time)
            latest--;
        if (latest == lines.Count - 1 && lines[^1].EndMs != 0 && time > lines[^1].EndMs)
            return lines.Count;
        return latest;
    }

    public static bool Active(DisplayLine line, double time) => !line.Untimed && time > line.StartMs && (line.EndMs == 0 || time <= line.EndMs);

    public static (double Start, double End)? BreathingGap(IReadOnlyList<DisplayLine> lines, double time)
    {
        if (lines.Count == 0)
            return null;
        if (lines[0].StartMs >= 6000 && time < lines[0].StartMs)
            return (0, lines[0].StartMs);
        for (int i = 0; i < lines.Count - 1; i++)
        {
            double end = lines[i].EndMs;
            if (end > 0 && lines[i + 1].StartMs - end > 6000 && time > end && time < lines[i + 1].StartMs)
                return (end, lines[i + 1].StartMs);
        }
        return null;
    }

    public static double Fraction(LyricWord word, double time, Func<string, double>? measure = null)
    {
        if (time <= word.StartMs)
            return 0;
        if (time >= word.EndMs || word.DurationMs <= 0)
            return 1;
        if (word.Syllables.Count == 0)
            return Math.Clamp((time - word.StartMs) / word.DurationMs, 0, 1);
        measure ??= text => text.Length;
        double total = measure(string.Concat(word.Syllables.Select(x => x.Text)));
        if (total <= 0)
            return Math.Clamp((time - word.StartMs) / word.DurationMs, 0, 1);
        string before = "";
        foreach (var syllable in word.Syllables)
        {
            double prefix = measure(before);
            string through = before + syllable.Text;
            if (time < syllable.StartMs)
                return Math.Clamp(prefix / total, 0, 1);
            if (time <= syllable.EndMs)
            {
                double p = syllable.DurationMs <= 0 ? 1 : Math.Clamp((time - syllable.StartMs) / syllable.DurationMs, 0, 1);
                return Math.Clamp((prefix + (measure(through) - prefix) * p) / total, 0, 1);
            }
            before = through;
        }
        return 1;
    }

    public static bool Emphasis(LyricWord word, bool last, MotionPreset preset)
    {
        string text = word.Text.Trim();
        int n = text.Length;
        bool cjk = n == 1 && IsHan(text[0]);
        if (preset != MotionPreset.Lyricify)
            return word.DurationMs > 1000 && n is > 0 && n <= ((last || preset == MotionPreset.Android) ? 7 : 6) && !cjk;
        double d = word.DurationMs;
        return n < 8 && ((n > 5 && d / n > 190) || (n is > 3 and <= 5 && d > 1100) || (n == 3 && d > 1225) ||
            (n == 2 && d > 1350) || (d > 1600 && n > 1 && d / n > 180) || (n == 1 && d > (cjk ? 2800 : 1500)));
    }

    public static bool IsCjk(char c) => c is >= '\u0800' and <= '\u9fff' or >= '\uac00' and <= '\ud7ff';
    public static bool IsHan(char c) => c is >= '\u4e00' and <= '\u9fff';
    public static bool Emphasis(IReadOnlyList<LyricWord> words, int index, bool right, MotionPreset preset)
    {
        var word = words[index];
        if (word.Text.Length == 0)
            return false;
        var previous = index > 0 ? words[index - 1] : null;
        var next = index + 1 < words.Count ? words[index + 1] : null;
        bool currentHan = word.Text.Any(IsHan);
        if (right)
        {
            if (previous is not null && word.Text[0] != ' ' && !previous.Text.Any(IsHan))
                return false;
            if (next is not null && next.Text.Length > 0 && next.Text[0] != ' ' && !currentHan)
                return false;
        }
        else
        {
            if (previous is not null && previous.Text.Length > 0 && previous.Text[^1] != ' ' && !previous.Text.Any(IsHan))
                return false;
            if (next is not null && word.Text[^1] != ' ' && !currentHan)
                return false;
        }
        if (preset == MotionPreset.Lyricify && next is not null && word.Text.Trim().Length > 6)
            return false;
        return Emphasis(word, next is null, preset);
    }
    public static string Format(double ms) => $"{(int)Math.Max(ms, 0) / 60000}:{(int)Math.Max(ms, 0) / 1000 % 60:00}";
}
