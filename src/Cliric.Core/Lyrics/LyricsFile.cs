using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Cliric.Core;

public static partial class LyricsFile
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static Song Load(string path, bool useCliricExtendedParser = false)
    {
        var info = new FileInfo(path);
        if (info.Length > 5 * 1024 * 1024)
            throw new FormatException("歌词文件超过 5 MB。");
        string text = File.ReadAllText(path);
        Song song = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => JsonSerializer.Deserialize<Song>(text, JsonOptions) ?? throw new FormatException("JSON 歌词为空。"),
            ".ttml" or ".xml" => useCliricExtendedParser ? CliricExtendedParser.ParseTtml(text) : ParseTtml(text),
            _ => useCliricExtendedParser ? CliricExtendedParser.ParseLrc(text) : ParseLrc(text)
        };
        if (song.Title == "未命名歌曲")
            song.Title = Path.GetFileNameWithoutExtension(path);
        string folder = info.DirectoryName!;
        song.AudioPath = ResolveMediaPath(folder, song.AudioPath);
        song.CoverPath = ResolveMediaPath(folder, song.CoverPath);
        song.ArtworkVideoPath = ResolveMediaPath(folder, song.ArtworkVideoPath);
        song.Validate();
        return song;
    }
    private static string? ResolveMediaPath(string folder, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new FormatException("歌词中的媒体路径必须是本地文件。");
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(folder, path));
    }

    public static Song ParseLrc(string text)
    {
        var song = new Song();
        double offset = 0;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            var meta = Metadata().Match(raw);
            if (meta.Success)
            {
                switch (meta.Groups[1].Value.ToLowerInvariant())
                {
                    case "ti":
                        song.Title = meta.Groups[2].Value;
                        break;
                    case "ar":
                        song.Artist = meta.Groups[2].Value;
                        break;
                    case "al":
                        song.Album = meta.Groups[2].Value;
                        break;
                    case "by":
                        song.Credit = meta.Groups[2].Value;
                        break;
                    case "offset":
                        double.TryParse(meta.Groups[2].Value, CultureInfo.InvariantCulture, out offset);
                        break;
                }
                continue;
            }
            var stamps = LrcStamp().Matches(raw);
            if (stamps.Count == 0)
                continue;
            int contentStart = stamps[^1].Index + stamps[^1].Length;
            string content = raw[contentStart..];
            foreach (Match stamp in stamps)
            {
                double start = ParseTime(stamp.Groups[1].Value);
                var line = new LyricLine { StartMs = start, Text = WordStamp().Replace(content, "") };
                var words = WordStamp().Matches(content);
                for (int i = 0; i < words.Count; i++)
                {
                    int a = words[i].Index + words[i].Length;
                    int b = i + 1 < words.Count ? words[i + 1].Index : content.Length;
                    string word = content[a..b];
                    if (word.Length == 0)
                        continue;
                    double begin = ParseTime(words[i].Groups[1].Value);
                    double end = i + 1 < words.Count ? ParseTime(words[i + 1].Groups[1].Value) : begin;
                    line.Words.Add(new LyricWord { Text = word, StartMs = begin, DurationMs = Math.Max(0, end - begin) });
                }
                song.Lines.Add(line);
            }
        }
        song.Lines = song.Lines.OrderBy(x => x.StartMs).ToList();
        if (song.Lines.Count == 0)
        {
            song.Untimed = true;
            song.Lines = text.Replace("\r", "").Split('\n').Where(x => !string.IsNullOrWhiteSpace(x) && !Metadata().IsMatch(x)).Select(x => new LyricLine { Text = x.Trim() }).ToList();
            song.Validate();
            return song;
        }
        for (int i = 0; i < song.Lines.Count; i++)
        {
            var line = song.Lines[i];
            double end = i + 1 < song.Lines.Count ? song.Lines[i + 1].StartMs : Math.Max(line.StartMs + 5000, line.Words.LastOrDefault()?.EndMs ?? 0);
            line.EndMs = end;
            if (line.Words.LastOrDefault() is { DurationMs: 0 } last)
                last.DurationMs = Math.Max(0, end - last.StartMs);
            line.StartMs = Math.Max(0, line.StartMs + offset);
            line.EndMs = Math.Max(line.StartMs, line.EndMs + offset);
            foreach (var word in line.Words)
                word.StartMs = Math.Max(0, word.StartMs + offset);
        }
        song.Validate();
        return song;
    }

    public static Song ParseTtml(string text)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 5 * 1024 * 1024 };
        using (var bounds = XmlReader.Create(new StringReader(text), settings))
        while (bounds.Read())
        if (bounds.Depth > 64)
            throw new FormatException("TTML 嵌套超过 64 层。");
        using var reader = XmlReader.Create(new StringReader(text), settings);
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var song = new Song();
        foreach (var p in document.Descendants().Where(x => x.Name.LocalName == "p"))
        {
            string? begin = Attribute(p, "begin");
            if (begin is null)
                continue;
            double start = ParseTime(begin);
            double end = Attribute(p, "end") is { } e ? ParseTime(e) : start + ParseTime(Attribute(p, "dur") ?? "5s");
            string agent = Attribute(p, "agent") ?? "";
            var line = new LyricLine { StartMs = start, EndMs = end, AlignLeft = agent.Length == 0 ? null : !agent.EndsWith('2') };
            var backgrounds = new List<LyricLine>();
            foreach (var node in p.Nodes())
            {
                if (node is XText plain)
                {
                    string value = plain.Value.Replace("\n", " ").Replace("\r", "");
                    if (string.IsNullOrWhiteSpace(value) && line.Words.Count == 0)
                        continue;
                    line.Text += value;
                    if (line.Words.Count > 0)
                        line.Words[^1].Text += value;
                    continue;
                }
                if (node is not XElement span)
                    continue;
                string role = Attribute(span, "role") ?? "";
                if (role.Contains("translation", StringComparison.OrdinalIgnoreCase))
                {
                    line.Translation = span.Value;
                    continue;
                }
                if (role.Contains("roman", StringComparison.OrdinalIgnoreCase))
                {
                    line.Romanization = span.Value;
                    continue;
                }
                if (role.Contains("bg", StringComparison.OrdinalIgnoreCase))
                {
                    var backing = ParseSpan(span, start, end);
                    backgrounds.Add(new LyricLine { Text = span.Value, StartMs = backing.StartMs, EndMs = backing.EndMs, Background = true, Words = [backing], AlignLeft = line.AlignLeft });
                    continue;
                }
                line.Text += span.Value;
                if (Attribute(span, "begin") is not null || span.Descendants().Any(x => Attribute(x, "begin") is not null))
                    line.Words.Add(ParseSpan(span, start, end));
            }
            line.Text = line.Text.Trim();
            song.Lines.Add(line);
            song.Lines.AddRange(backgrounds);
        }
        song.Validate();
        return song;
    }

    private static LyricWord ParseSpan(XElement span, double parentStart, double parentEnd)
    {
        double start = Attribute(span, "begin") is { } b ? ParseTime(b) : parentStart;
        double end = Attribute(span, "end") is { } e ? ParseTime(e) : Attribute(span, "dur") is { } d ? start + ParseTime(d) : parentEnd;
        var word = new LyricWord { Text = span.Value, StartMs = start, DurationMs = Math.Max(0, end - start) };
        foreach (var child in span.Elements().Where(x => Attribute(x, "begin") is not null))
        {
            var item = ParseSpan(child, start, end);
            word.Syllables.Add(new Syllable { Text = item.Text, StartMs = item.StartMs, DurationMs = item.DurationMs });
        }
        return word;
    }

    private static string? Attribute(XElement element, string name) => element.Attributes().FirstOrDefault(x => x.Name.LocalName == name)?.Value;

    public static double ParseTime(string value)
    {
        value = value.Trim();
        if (value.EndsWith("ms", StringComparison.Ordinal))
            return double.Parse(value[..^2], CultureInfo.InvariantCulture);
        if (value.EndsWith('s'))
            return double.Parse(value[..^1], CultureInfo.InvariantCulture) * 1000;
        if (value.EndsWith('m'))
            return double.Parse(value[..^1], CultureInfo.InvariantCulture) * 60000;
        var fields = value.Split(':');
        double result = 0;
        foreach (var field in fields)
            result = result * 60 + double.Parse(field, CultureInfo.InvariantCulture);
        return result * 1000;
    }

    [GeneratedRegex(@"^\[(ti|ar|al|by|offset):([^\]]*)\]", RegexOptions.IgnoreCase)] private static partial Regex Metadata();
    [GeneratedRegex(@"\[(\d+:\d+(?:\.\d+)?)\]")] private static partial Regex LrcStamp();
    [GeneratedRegex(@"<(\d+:\d+(?:\.\d+)?)>")] private static partial Regex WordStamp();
}
