namespace Cliric.Core;

public static class WordSweep
{
    public static double Dispatched(WrappedLine line, int index, double startedMs, double from, double time, double scale, double feather, bool chinese, Func<string, double>? measure = null) => Dispatch(line, index, startedMs, from, scale, feather, chinese, measure).At(time);
    public static WordSweepPlan Dispatch(WrappedLine line, int index, double startedMs, double from, double scale, double feather, bool chinese, Func<string, double>? measure = null)
    {
        int local = -1;
        for (int i = 0; i < line.Words.Count; i++)
        if (line.Words[i].Index == index)
        {
            local = i;
            break;
        }
        if (local < 0)
            return new(startedMs, 0, from, from, false, []);
        var item = line.Words[local];
        var word = item.Word;
        double total = line.Width + 40 * scale, advance = line.Words.Take(local).Sum(w => w.Size.Width), side = 20 * scale;
        if (total <= 0)
            return new(startedMs, 0, from, from, false, []);
        double target = (Math.Max(0, advance + item.Size.InkWidth - (local == line.Words.Count - 1 ? 0 : feather * scale / 2)) + side) / total;
        double duration = Math.Max(0, word.EndMs - startedMs);
        if (word.Syllables.Count == 0)
            return new(startedMs, duration, from, target, chinese, []);
        measure ??= text => item.Size.InkWidth * text.Length / Math.Max(1, word.Text.Length);
        var points = new List<(double TimeMs, double Value)> { (0, from) };
        double space = word.Text.StartsWith(' ') ? measure(" ") : 0;
        string prefix = "";
        for (int j = 0; j < word.Syllables.Count - 1; j++)
        {
            var syllable = word.Syllables[j];
            prefix += syllable.Text;
            double point = (advance + space + measure(prefix) - feather * scale / 2 + side) / total, end = Math.Min(syllable.EndMs - word.StartMs, duration);
            points.Add((end, point));
            double next = word.Syllables[j + 1].StartMs - word.StartMs;
            if (next < duration)
                points.Add((next, point));
        }
        points.Add((duration, target));
        return new(startedMs, duration, from, target, false, points.OrderBy(point => point.TimeMs).ToArray());
    }
    public static double NextLineStart(IReadOnlyList<DisplayLine> lines, int index)
    {
        if (index + 1 >= lines.Count)
            return -1;
        var current = lines[index];
        var next = lines[index + 1];
        if (current.EndMs <= next.StartMs + 323)
            return next.StartMs;
        return current.Backgrounds.Count == 0 && index + 2 < lines.Count ? lines[index + 2].StartMs : -1;
    }
    public static double At(WrappedLine line, double time, double scale, double feather, bool chinese, Func<string, double>? measure = null)
    {
        double total = line.Width + 40 * scale;
        if (total <= 0 || line.Words.Count == 0)
            return 0;
        double previous = 0, advance = 0, side = 20 * scale;
        for (int i = 0; i < line.Words.Count; i++)
        {
            var item = line.Words[i];
            var word = item.Word;
            if (time < word.StartMs)
                return previous;
            double target = (Math.Max(0, advance + item.Size.InkWidth - (i == line.Words.Count - 1 ? 0 : feather * scale / 2)) + side) / total;
            if (time >= word.EndMs || word.DurationMs <= 0)
            {
                previous = target;
                advance += item.Size.Width;
                continue;
            }
            double elapsed = time - word.StartMs;
            if (word.Syllables.Count > 0)
            {
                measure ??= text => item.Size.InkWidth * text.Length / Math.Max(1, word.Text.Length);
                double fromTime = 0, from = previous, space = word.Text.StartsWith(' ') ? measure(" ") : 0;
                string prefix = "";
                for (int j = 0; j < word.Syllables.Count - 1; j++)
                {
                    var syllable = word.Syllables[j];
                    prefix += syllable.Text;
                    double point = (advance + space + measure(prefix) - feather * scale / 2 + side) / total;
                    double end = Math.Clamp(syllable.EndMs - word.StartMs, 0, word.DurationMs);
                    if (elapsed < end)
                        return Lerp(from, point, elapsed - fromTime, end - fromTime);
                    from = point;
                    fromTime = end;
                    double next = Math.Clamp(word.Syllables[j + 1].StartMs - word.StartMs, end, word.DurationMs);
                    if (elapsed < next)
                        return point;
                    fromTime = next;
                }
                return Lerp(from, target, elapsed - fromTime, word.DurationMs - fromTime);
            }
            double fraction = Math.Clamp(elapsed / word.DurationMs, 0, 1);
            if (chinese)
                fraction = 1 - Math.Pow(1 - fraction, 1.21);
            return previous + (target - previous) * fraction;
        }
        return previous;
    }
    private static double Lerp(double from, double to, double elapsed, double duration) => duration <= 0 ? to : from + (to - from) * Math.Clamp(elapsed / duration, 0, 1);
}

public sealed class WordRunState
{
    private long _generation = long.MinValue;
    private double _previousTime = double.NaN;
    private bool _running;
    private List<WordDispatch> _dispatches = [];
    public int CurrentWord { get; private set; } = -1;
    public double FinishStartedMs { get; private set; } = double.NaN;
    public double FinishDurationMs
    {
        get; private set;
    }
    public double FinishFromPositionMs { get; private set; } = double.NaN;
    public double PositionMs => _previousTime;
    public bool Finishing => !double.IsNaN(FinishStartedMs);
    public WordDispatch? DispatchFor(int index) => index >= 0 && index < _dispatches.Count ? _dispatches[index] : null;
    public WordRunState Snapshot()
    {
        var copy = (WordRunState)MemberwiseClone();
        copy._dispatches = [.. _dispatches];
        return copy;
    }
    public void Stop() => _running = false;

    public bool Advance(double time, IReadOnlyList<LyricWord> words, double nextStart, bool ios164, long generation,
        bool running = true, bool completeLastWord = false)
    {
        bool reset = generation != _generation || time < _previousTime || running && !_running;
        double previous = reset ? double.NaN : _previousTime;
        if (reset)
        {
            CurrentWord = -1;
            FinishStartedMs = double.NaN;
            FinishDurationMs = 0;
            FinishFromPositionMs = double.NaN;
            _dispatches.Clear();
        }
        _generation = generation;
        _previousTime = time;
        _running = running;
        if (running && !Finishing)
        {
            if (completeLastWord || nextStart < 0 || nextStart - time >= 500 || CurrentWord >= words.Count - 1 && ios164)
            {
                int first = CurrentWord + 1;
                while (CurrentWord < words.Count - 1 && words[CurrentWord + 1].StartMs <= time)
                {
                    CurrentWord++;
                    _dispatches.Add(new(time, CurrentWord == words.Count - 1 ? Math.Max(0, words[CurrentWord].EndMs - time) : words[CurrentWord].DurationMs));
                }
                // The original resumes prefix measurements against the shared current word after selecting later words.
                // shortcut: prefix completions are coalesced per render update; validate actual task timing under thread saturation before claiming identical scheduling.
                for (int index = first; index < CurrentWord; index++)
                    if (words[index].Syllables.Count > 1 || words[index].Syllables.Count > 0 && words[index].Text.StartsWith(' '))
                        _dispatches[index] = _dispatches[index] with
                        {
                            ShineStarted = false
                        };
            }
            else
            {
                FinishStartedMs = time;
                FinishDurationMs = Math.Max(0, nextStart - time - 275);
                FinishFromPositionMs = previous;
            }
        }
        return reset;
    }
    public double FinishProgress(double from, double time) => FinishDurationMs <= 0 ? 1 : from + (1 - from) * Math.Clamp((time - FinishStartedMs) / FinishDurationMs, 0, 1);
}

public readonly record struct WordDispatch(double StartedMs, double DurationMs, bool ShineStarted = true);

public sealed record WordSweepPlan(double StartedMs, double DurationMs, double From, double To, bool Chinese, IReadOnlyList<(double TimeMs, double Value)> KeyFrames)
{
    public double At(double time)
    {
        double elapsed = Math.Max(0, time - StartedMs);
        if (DurationMs <= 0 || elapsed >= DurationMs)
            return To;
        if (KeyFrames.Count == 0)
        {
            double fraction = elapsed / DurationMs;
            if (Chinese)
                fraction = 1 - Math.Pow(1 - fraction, 1.21);
            return From + (To - From) * fraction;
        }
        var previous = KeyFrames[0];
        foreach (var point in KeyFrames.Skip(1))
        {
            if (elapsed < point.TimeMs)
                return previous.Value + (point.Value - previous.Value) * Math.Clamp((elapsed - previous.TimeMs) / (point.TimeMs - previous.TimeMs), 0, 1);
            previous = point;
        }
        return To;
    }
}
