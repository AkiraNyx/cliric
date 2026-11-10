using System.Text.Json;
using LyricReplica.Core;

static class WordSweepChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/word-sweep-original.json")));
        foreach (var sample in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var words = sample.GetProperty("words").EnumerateArray().Select((w, i) => new MeasuredWord(
                new LyricWord { Text = w.GetProperty("text").GetString()!, StartMs = w.GetProperty("start").GetDouble(), DurationMs = w.GetProperty("duration").GetDouble() }, i,
                new TextMeasure(w.GetProperty("width").GetDouble(), w.GetProperty("ink").GetDouble(), 50))).ToArray();
            var layout = new WrappedLine(words, words.Sum(w => w.Size.Width));
            var word = words[sample.GetProperty("selected").GetInt32()].Word;
            foreach (var frame in sample.GetProperty("samples").EnumerateArray())
            {
                double time = word.StartMs + word.DurationMs * frame.GetProperty("fraction").GetDouble();
                double value = WordSweep.At(layout, time, 1, sample.GetProperty("feather").GetDouble(), true);
                check(Math.Abs(value - frame.GetProperty("value").GetDouble()) < 1e-9, "original Run sweep curve " + sample.GetProperty("lineStart") + " / " + word.Text + " / " + time);
                double dispatched = WordSweep.Dispatched(layout, sample.GetProperty("selected").GetInt32(), word.StartMs, sample.GetProperty("from").GetDouble(), time, 1, sample.GetProperty("feather").GetDouble(), true);
                check(Math.Abs(dispatched - frame.GetProperty("value").GetDouble()) < 1e-9, "event sweep also preserves the original full-duration word curve");
            }
            check(WordSweep.At(layout, words[0].Word.StartMs - 1, 1, 20, true) == 0, "first glyph sweep starts outside the ink");
            double final = WordSweep.At(layout, words[^1].Word.EndMs + 1, 1, 20, true);
            check(Math.Abs(final - (layout.Width + 20) / (layout.Width + 40)) < 1e-9, "tail sweep reaches the full ink edge");
        }
        var syllables = new LyricWord { Text = "hello", StartMs = 1000, DurationMs = 1000, Syllables = [new() { Text = "he", StartMs = 1000, DurationMs = 400 }, new() { Text = "llo", StartMs = 1600, DurationMs = 400 }] };
        var single = new WrappedLine([new(syllables, 0, new(100, 100, 50))], 100);
        double Measure(string text) => text.Length * 20;
        double prefix = (40 - 10 + 20) / 140d;
        check(Math.Abs(WordSweep.At(single, 1500, 1, 20, false, Measure) - prefix) < 1e-9, "sub-syllable sweep holds the feather-adjusted prefix during the gap");
        check(Math.Abs(WordSweep.At(single, 1800, 1, 20, false, Measure) - (prefix + (120 / 140d - prefix) / 2)) < 1e-9, "last syllable sweeps all the way to the tail");
        double late = WordSweep.At(single, 1800, 1, 20, false, Measure);
        WordSweep.At(single, 1100, 1, 20, false, Measure);
        check(WordSweep.At(single, 1800, 1, 20, false, Measure) == late, "sweep reconstruction is independent of seek order");
        using var stops = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/word-stop-original.json")));
        foreach (var original in stops.RootElement.GetProperty("cases").EnumerateArray())
        {
            double response = original.GetProperty("response").GetDouble(), multiplier = original.GetProperty("multiplier").GetDouble();
            foreach (var frame in original.GetProperty("frames").EnumerateArray())
            {
                // Map normalized source progress through its quantized TimeSpan duration.
                double elapsed = frame.GetProperty("nativeProgress").GetDouble() * TimeSpan.FromSeconds(response * multiplier).TotalMilliseconds;
                var pose = Motion.StopGlyph((1.1, -7, .4), elapsed, response, multiplier, original.GetProperty("lift").GetDouble(), original.GetProperty("immediate").GetBoolean());
                check(Math.Abs(pose.Scale - frame.GetProperty("scale").GetDouble()) < 1e-7 && Math.Abs(pose.Y - frame.GetProperty("y").GetDouble()) < 1e-7, $"original Stop smooth and immediate glyph pose: response={response}, multiplier={multiplier}, fraction={frame.GetProperty("fraction")}, immediate={original.GetProperty("immediate")}, actual=({pose.Scale},{pose.Y}), expected=({frame.GetProperty("scale")},{frame.GetProperty("y")})");
            }
        }
        var fragment = new List<LyricWord> { new() { Text = "hel", DurationMs = 2000 }, new() { Text = "lo ", DurationMs = 2000 } };
        check(!Timeline.Emphasis(fragment, 0, false, MotionPreset.Ios) && !Timeline.Emphasis(fragment, 1, false, MotionPreset.Ios), "split Latin fragments do not start independent emphasis");
        check(Timeline.Emphasis(new LyricWord { Text = "カ", DurationMs = 2000 }, true, MotionPreset.Ios), "reference emphasis excludes single Han glyphs rather than every CJK script");
        check(Math.Abs(FloatingMotion.Y(5000, 2000, 1000, 1000, 41, false, 4) + 4) < 1e-9, "configured float height reaches its source target");
    }
}
