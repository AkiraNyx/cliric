using System.Globalization;
using System.Text.RegularExpressions;

namespace LyricReplica.Core;

public static partial class YrcLyrics
{
    public static List<LyricLine> Parse(string text)
    {
        if (text.Length > 5 * 1024 * 1024)
            throw new FormatException("逐字歌词超过 5 MB。");
        var lines = new List<LyricLine>();
        foreach (string raw in text.Split('\n'))
        {
            string content = raw.TrimEnd('\r');
            var header = LineHeader().Match(content);
            if (!header.Success)
                continue;
            double start = Milliseconds(header.Groups[1].Value);
            var line = new LyricLine { StartMs = start, EndMs = start + Milliseconds(header.Groups[2].Value) };
            var words = WordMarker().Matches(content, header.Length);
            for (int i = 0; i < words.Count; i++)
            {
                var marker = words[i];
                int endOfMarker = marker.Index + marker.Length;
                int nextMarker = i + 1 < words.Count ? words[i + 1].Index : content.Length;
                if (nextMarker == endOfMarker)
                    continue;
                line.Words.Add(new LyricWord
                {
                    Text = content[endOfMarker..nextMarker],
                    StartMs = Milliseconds(marker.Groups[1].Value),
                    DurationMs = Milliseconds(marker.Groups[2].Value)
                });
            }
            if (line.Words.Count == 0)
                continue;
            line.Text = string.Concat(line.Words.Select(word => word.Text));
            lines.Add(line);
        }
        var song = new Song { Lines = lines.OrderBy(line => line.StartMs).ToList() };
        song.Validate();
        return song.Lines;
    }

    private static double Milliseconds(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\[(\d+),(\d+)\]")]
    private static partial Regex LineHeader();

    [GeneratedRegex(@"\((\d+),(\d+),\d+\)")]
    private static partial Regex WordMarker();
}
