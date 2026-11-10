using System.Globalization;

namespace LyricReplica.Core;

public sealed record TextMeasure(double Width, double InkWidth, double Height);
public sealed record MeasuredWord(LyricWord Word, int Index, TextMeasure Size);
public sealed record WrappedLine(IReadOnlyList<MeasuredWord> Words, double Width);

public static class LineBreaker
{
    public static IReadOnlyList<WrappedLine> Wrap(IReadOnlyList<LyricWord> words, double maxWidth, Func<string, TextMeasure> measure)
    {
        if (!double.IsFinite(maxWidth) || maxWidth <= 0)
            maxWidth = 1;
        if (words.Count == 0)
            return [];
        var sizes = words.Select((w, i) => new MeasuredWord(w, i, measure(w.Text))).ToArray();
        var lines = new int[words.Count];
        int line = 1;
        double accumulated = 0;
        for (int i = 0; i < words.Count; i++)
        {
            if (i > 0)
                accumulated += sizes[i - 1].Size.Width - sizes[i - 1].Size.InkWidth;
            accumulated += sizes[i].Size.InkWidth;
            if (accumulated > maxWidth && i > 0)
            {
                if (!Boundary(words[i - 1].Text, true) && !Boundary(words[i].Text, false))
                {
                    int back = i;
                    double tail = 0;
                    while (--back >= 0)
                    {
                        if (lines[back] == line)
                        {
                            if (!Boundary(words[back].Text, true) && !Boundary(words[back + 1].Text, false))
                            {
                                tail += sizes[back].Size.Width;
                                continue;
                            }
                            line++;
                            accumulated = tail + sizes[i].Size.InkWidth;
                            for (int j = back + 1; j < i; j++)
                                lines[j] = line;
                            if (accumulated > maxWidth)
                            {
                                tail = 0;
                                for (int j = back + 1; j < i; j++)
                                {
                                    if (tail + sizes[j].Size.Width > maxWidth)
                                    {
                                        line++;
                                        for (int k = j; k < i; k++)
                                            lines[k] = line;
                                        tail = sizes[j].Size.Width;
                                    }
                                    else
                                        tail += sizes[j].Size.Width;
                                }
                            }
                            break;
                        }
                        line++;
                        accumulated = sizes[i].Size.InkWidth;
                        break;
                    }
                }
                else
                {
                    line++;
                    accumulated = sizes[i].Size.InkWidth;
                }
            }
            lines[i] = line;
        }
        return sizes.GroupBy(w => lines[w.Index]).Select(g => new WrappedLine(g.ToArray(), g.Sum(w => w.Size.Width))).ToArray();
    }

    public static bool Boundary(string text, bool end)
    {
        if (string.IsNullOrEmpty(text))
            return true;
        char c = end ? text[^1] : text[0];
        var category = char.GetUnicodeCategory(c);
        return char.IsWhiteSpace(c) || Timeline.IsCjk(c) || category is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol;
    }

    public static double GlyphOrigin(int count, double wholeWidth, double advance, double charAdvance, double charWidth)
    {
        if (wholeWidth <= 0 || charWidth <= 0)
            return .5;
        double p = (advance - charAdvance / 2) / wholeWidth;
        double target = count switch
        {
            1 => .5,
            2 => -.1 * p + .55,
            3 => -.05 * p + .525,
            4 => .1 * p + .45,
            5 => .15 * p + .425,
            6 => .2 * p + .4,
            _ => .25 * p + .375
        };
        double result = (target * wholeWidth - advance + charWidth) / charWidth;
        return double.IsNormal(result) ? result : .5;
    }
}
