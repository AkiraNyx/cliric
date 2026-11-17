using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Parsers;

namespace LyricReplica.Core;

public static class LyricifyParser
{
    public static Song ParseLrc(string text)
    {
        var song = LyricsFile.ParseLrc(text);
        if (!song.Untimed && song.Lines.All(line => line.Words.Count == 0))
            Apply(song, () => LrcParser.Parse(text));
        return song;
    }

    public static Song ParseTtml(string text)
    {
        // Keep the built-in XML size/depth guard and metadata before invoking upstream.
        var song = LyricsFile.ParseTtml(text);
        Apply(song, () => TtmlParser.Parse(text, useEmbeddedSimplifiedChineseLyrics: false));
        return song;
    }

    public static List<LyricLine> ParseYrc(string text)
    {
        var lines = YrcLyrics.Parse(text);
        var content = string.Join('\n', text.Split('\n').Where(line => line.StartsWith('[')).Select(line => line.TrimEnd('\r')));
        var parsed = TryRead(() => YrcParser.ParseOnlyLyrics(content));
        if (parsed is null)
            return lines;
        var byWords = parsed.OfType<SyllableLineInfo>().Where(line => line.Syllables.Count > 0)
            .ToLookup(line => (line.Syllables[0].StartTime, line.Text));
        foreach (var line in lines)
        {
            var upstream = byWords[((int)line.Words[0].StartMs, line.Text)].FirstOrDefault();
            if (upstream is null)
                continue;
            var words = ReadWords(upstream);
            if (words.Count > 0 && words.All(word => word.StartMs >= 0 && word.DurationMs >= 0 && word.EndMs <= 86_400_000))
                line.Words = words;
        }
        return lines;
    }

    private static void Apply(Song song, Func<LyricsData> parse)
    {
        var parsed = TryRead(() => parse().Lines);
        if (parsed is not { Count: > 0 })
            return;
        var builtIn = song.Lines.ToLookup(line => (line.StartMs, line.Text, line.IsBackground));
        var lines = new List<LyricLine>();
        foreach (var source in parsed)
        {
            Add(source, background: false);
            if (source.SubLine is { } backing)
                Add(backing, background: true);
        }
        if (lines.Count == 0 || lines.Count(line => line.Words.Count > 0) < song.Lines.Count(line => line.Words.Count > 0) ||
            lines.Count(line => line.IsBackground) < song.Lines.Count(line => line.IsBackground))
            return;
        var candidate = new Song { Lines = lines.OrderBy(line => line.StartMs).ToList(), Untimed = parsed.All(line => line.StartTime is null), DurationMs = song.DurationMs };
        try { candidate.Validate(); }
        catch (FormatException) { return; }
        song.Lines = candidate.Lines;
        song.Untimed = candidate.Untimed;
        song.DurationMs = candidate.DurationMs;

        void Add(ILineInfo source, bool background)
        {
            var line = Convert(source, background);
            var previous = builtIn[(line.StartMs, line.Text, line.IsBackground)].FirstOrDefault();
            if (source.EndTime is null)
                line.EndMs = previous?.EndMs ?? Math.Min(line.StartMs + 5000, 86_400_000);
            if (previous is not null)
            {
                if (line.Translation.Length == 0)
                    line.Translation = previous.Translation;
                if (line.Romanization.Length == 0)
                    line.Romanization = previous.Romanization;
            }
            lines.Add(line);
        }
    }

    private static List<ILineInfo>? TryRead(Func<List<ILineInfo>?> parse)
    {
        try { return parse(); }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException or OverflowException or IndexOutOfRangeException or System.Xml.XmlException or Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    private static LyricLine Convert(ILineInfo source, bool background = false)
    {
        var full = source as IFullLineInfo;
        return new LyricLine
        {
            Text = source.Text,
            Translation = full?.ChineseTranslation ?? full?.Translations.Values.FirstOrDefault() ?? "",
            Romanization = full?.Pronunciation ?? "",
            StartMs = source.StartTime ?? 0,
            EndMs = source.EndTime ?? source.StartTime ?? 0,
            Background = background ? true : null,
            AlignLeft = source.LyricsAlignment == LyricsAlignment.Unspecified ? null : source.LyricsAlignment == LyricsAlignment.Left,
            Words = source is SyllableLineInfo timed ? ReadWords(timed) : []
        };
    }

    private static List<LyricWord> ReadWords(SyllableLineInfo line) => line.Syllables.Where(word => word.Text.Length > 0)
        .Select(word => new LyricWord { Text = word.Text, StartMs = word.StartTime, DurationMs = word.Duration }).ToList();
}
