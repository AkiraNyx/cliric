using System.Text.Json;
using Cliric.Core;

static class LineSettingsChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/line-settings-original.json")));
        var settings = new AppleMusicSettings();
        foreach (var item in fixture.RootElement.GetProperty("zoom").EnumerateArray())
        {
            settings.HighlightLineZooming = item.GetProperty("preset").GetInt32();
            settings.BlurLyrics = item.GetProperty("blur").GetBoolean();
            settings.PlayingZoomOverride = item.GetProperty("override").ValueKind == JsonValueKind.String ? double.NaN : item.GetProperty("override").GetDouble();
            check(Math.Abs(settings.GetHighlightScale(item.GetProperty("timed").GetBoolean()) - item.GetProperty("value").GetDouble()) < 1e-9, "original highlight style and override");
        }
        foreach (var item in fixture.RootElement.GetProperty("secondary").EnumerateArray())
        {
            settings.TranslationLinePreset = (TranslationLinePresetTypes)item.GetProperty("preset").GetInt32();
            check(Math.Abs(settings.GetSecondaryAlpha(item.GetProperty("timed").GetBoolean(), item.GetProperty("background").GetBoolean()) - item.GetProperty("value").GetDouble()) < 1e-9, "original translation style target");
        }
        foreach (var item in fixture.RootElement.GetProperty("cjk").EnumerateArray())
        {
            var lines = item.GetProperty("texts").EnumerateArray().Select(x => new LyricLine { Text = x.GetString()! }).ToList();
            check(LyricsPreparation.IsCjkLyrics(lines) == item.GetProperty("value").GetBoolean(), "original CJK page threshold: " + item.ToString());
        }
        var song = new Song { Lines = [new() { Text = "中文", StartMs = 1000, EndMs = 2000, Words = [new() { Text = "中文", StartMs = 1000, DurationMs = 1000 }] }] };
        settings = new();
        check(LyricsPreparation.UseKaraoke(song, settings), "default timed song enables karaoke");
        var prepared = LyricsPreparation.ForPage(song, true, false, settings);
        settings.Karaoke = false;
        check(!LyricsPreparation.UseKaraoke(song, settings) && LyricsPreparation.ForPage(song, true, false, settings)[0].StartMs == 1000 && prepared[0].StartMs != 1000, "karaoke switch changes both rendering mode and line preparation");
        settings.Karaoke = true;
        settings.IsCjkKaraokeDisabled = true;
        check(!LyricsPreparation.UseKaraoke(song, settings) && song.Lines[0].Words.Count == 1, "CJK karaoke exception preserves raw word data");
        song.Lines[0].Text = "hello";
        check(LyricsPreparation.UseKaraoke(song, settings), "CJK exception leaves a non-CJK song in karaoke");
        settings = new()
        {
            HighlightLineZooming = 2,
            CurrentLinePreplayOpacityPreset = 1,
            TranslationLinePreset = TranslationLinePresetTypes.iOS
        };
        var frame = LineMotion.At(3000, 1000, 4000, true, false, false, reduceMotion: true, settings: settings);
        check(frame.ContentScale == 1.1 && Math.Abs(frame.UnplayedAlpha - 95d / 255) < 1e-9 && Math.Abs(frame.SecondaryAlpha - 95d / 255) < 1e-9, "style settings reach the live line frame");
        frame = LineMotion.At(3000, 1000, 4000, true, true, false, reduceMotion: true, settings: settings);
        check(Math.Abs(frame.PlayedAlpha - 159d / 255) < 1e-9, "backing karaoke uses the original sung-word alpha");
        settings.UnplayingOpacityOverride = .25;
        frame = LineMotion.At(0, 1000, 4000, false, false, false, settings: settings);
        check(frame.PlayedAlpha == .25, "plain line opacity override reaches the frame");
        using var shiny = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/shiny-words-original.json")));
        foreach (var item in shiny.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = JsonSerializer.Deserialize<List<LyricWord>>(item.GetProperty("input").GetRawText(), LyricsFile.JsonOptions)!;
            var expected = JsonSerializer.Deserialize<List<LyricWord>>(item.GetProperty("output").GetRawText(), LyricsFile.JsonOptions)!;
            string before = JsonSerializer.Serialize(input);
            var result = ShinyWords.ForDisplay(new LyricLine { Text = string.Concat(input.Select(w => w.Text)), Words = input });
            check(JsonSerializer.Serialize(result.Words) == JsonSerializer.Serialize(expected), "original CJK emphasis grouping and seven-character limit");
            check(before == JsonSerializer.Serialize(input), "emphasis grouping preserves imported word and syllable data");
        }
    }
}
