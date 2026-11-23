namespace Cliric.Core;

public static class ShinyWords
{
    public static LyricLine ForDisplay(LyricLine source)
    {
        if (source.Words.Count < 2)
            return source;
        var words = source.Words.ToList();
        for (int i = 0; i < words.Count - 1; i++)
        {
            var first = words[i];
            if (Boundary(first.Text))
                continue;
            var group = new LyricWord { Text = first.Text, StartMs = first.StartMs, DurationMs = first.DurationMs };
            var pieces = new List<LyricWord> { new() { Text = first.Text, StartMs = first.StartMs, DurationMs = first.DurationMs } };
            group.Syllables.Add(Syllable(first));
            words[i] = group;
            i++;
            do
            {
                var next = words[i];
                pieces.Add(next);
                group.Syllables.Add(Syllable(next));
                words.RemoveAt(i);
                group.Text += next.Text;
                group.DurationMs = next.EndMs - group.StartMs;
                if (group.Text.Trim().Length <= 7)
                    continue;
                int index = words.IndexOf(group);
                words.RemoveAt(index);
                foreach (var piece in pieces)
                {
                    words.Insert(index++, piece);
                    i++;
                }
                for (i -= 2; i < words.Count && !Boundary(words[i].Text); i++)
                {
                }
                break;
            } while (!Boundary(group.Text) && i < words.Count);
            i--;
        }
        return new LyricLine { Id = source.Id, Text = source.Text, Translation = source.Translation, Romanization = source.Romanization, StartMs = source.StartMs, EndMs = source.EndMs, Background = source.Background, AlignLeft = source.AlignLeft, Words = words };
    }
    private static bool Boundary(string text) => text.EndsWith(' ') || text.EndsWith(',') || text.EndsWith('/');
    private static Syllable Syllable(LyricWord word) => new() { Text = word.Text, StartMs = word.StartMs, DurationMs = word.DurationMs };
}
