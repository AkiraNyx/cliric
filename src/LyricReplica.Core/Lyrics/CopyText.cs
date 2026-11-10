namespace LyricReplica.Core;

public enum CopyKind
{
    Line, Translation, LineWithTranslation, AllLines, AllTranslations, AllWithTranslations
}

public static class CopyText
{
    public static string Build(CopyKind kind, LyricLine selected, IEnumerable<LyricLine> complete, IEnumerable<LyricLine> displayed)
    {
        string lineBreak = Environment.NewLine;
        return kind switch
        {
            CopyKind.Line => selected.Text,
            CopyKind.Translation => selected.Translation,
            CopyKind.LineWithTranslation => selected.Text + lineBreak + selected.Translation,
            CopyKind.AllLines => string.Join(lineBreak, complete.Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x))).Trim(),
            CopyKind.AllTranslations => string.Join(lineBreak, complete.Select(x => x.Translation).Where(x => !string.IsNullOrWhiteSpace(x))).Trim(),
            CopyKind.AllWithTranslations => string.Join(lineBreak + lineBreak, displayed.Select(x => (x.Text + lineBreak + x.Translation).Trim()).Where(x => !string.IsNullOrWhiteSpace(x))).Trim(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
