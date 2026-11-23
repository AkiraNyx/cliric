using Cliric.Core;

int assertions = 0;
void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
void Near(double actual, double expected, string name) => Check(Math.Abs(actual - expected) < 1e-6, $"{name}: {actual} != {expected}");
SettingsChecks.Run(Check);
WordSweepChecks.Run(Check);
LineSettingsChecks.Run(Check);
PaletteChecks.Run(Check);
ZoomSettingsChecks.Run(Check);
LineOpacityChecks.Run(Check);
BackgroundStateChecks.Run(Check);
BreathingRunChecks.Run(Check);
EarlyFinishChecks.Run(Check);
GlyphStartChecks.Run(Check);
LegacyStopChecks.Run(Check);
LineGlyphStopChecks.Run(Check);
WordDispatchChecks.Run(Check);
PullingParameterChecks.Run(Check);
foreach (bool ios in new[] { false, true })
foreach (bool timed in new[] { false, true })
{
    var breathingSettings = new AppleMusicSettings { InterLineAnimationPreset = ios ? InterLineAnimationPresetTypes.iOS : InterLineAnimationPresetTypes.Lyricify, PullingEffect = true, PullingDelay = 2.5, AnimationSpeed = 1.5 };
    int scroll = ios ? (int)Math.Round(Motion.ScrollParameters().Duration * 1000) : timed ? 500 : 620;
    Near(Motion.BreathingEndExtensionMs(breathingSettings, timed), (int)(scroll * (ios ? 1d / 31 : .03) * 2.5 * 1.5), "page breathing compensation uses the actual karaoke scroll duration");
    breathingSettings.PullingEffect = false;
    Near(Motion.BreathingEndExtensionMs(breathingSettings, timed), 0, "pulling disabled has no breathing compensation");
}
var clockTime = 0d;
Check(SettingsFile.Parse("{\"Romanization\":true}").RomajiOption == RomajiOptions.RomajiFirst, "existing romanization preference migrates without losing its enabled state");
Check(ArtworkVideos.Resolve("4d0a5uamW9NWRc1KXFeErI") == "https://res.lyricify.app/videos/id/0SrskI3mHcu5MzKeZNv2f6.mp4" && ArtworkVideos.Resolve("6MBVShS0FIBbZLBdfzXuZS") == "https://res.lyricify.app/videos/id/4yHDaLGrPSwUfJNVUIvFgg.mp4" && ArtworkVideos.Resolve("3rZyOsowQOtW3BMuO4vCmh") == "https://res.lyricify.app/videos/id/3rZyOsowQOtW3BMuO4vCmh.mp4", "dynamic artwork uses the original runtime catalog and album aliases");
Check(ArtworkVideos.Resolve("unlisted") is null, "unlisted albums retain their static cover");
var overlapSong = new Song { Lines = [new() { Text = "one", StartMs = 1000, EndMs = 5000, Words = [new() { Text = "one", StartMs = 1000, DurationMs = 4000 }] }, new() { Text = "two", StartMs = 3000, EndMs = 6000, Words = [new() { Text = "two", StartMs = 3000, DurationMs = 3000 }] }] };
var multiSettings = new AppleMusicSettings();
var multiple = LyricsPreparation.ForPage(overlapSong, false, false, multiSettings, true);
var single = LyricsPreparation.ForPage(overlapSong, false, false, multiSettings, false);
Check(multiple[0].EndMs > multiple[1].StartMs && single[0].EndMs == single[1].StartMs, "global multiline switch changes overlapping karaoke boundaries");
multiSettings.InterLineAnimationPreset = InterLineAnimationPresetTypes.Lyricify;
var classic = LyricsPreparation.ForPage(overlapSong, false, false, multiSettings, false);
Near(classic[0].StartMs, 700, "legacy page uses its original fixed 300ms entry lead");
Check(classic[0].EndMs == classic[1].StartMs, "legacy page respects the global single-line setting");
var clock = new PlaybackClock(() => clockTime);
clock.Load(10000);
clock.Play();
clockTime = 1000;
Near(clock.PositionMs, 1000, "play");
clock.Pause();
clockTime = 4000;
Near(clock.PositionMs, 1000, "pause");
clock.Play();
clockTime = 4500;
Near(clock.PositionMs, 1500, "resume");
clock.Seek(700);
Near(clock.PositionMs, 700, "rewind");
long generation = clock.Generation;
clock.Seek(6000);
Check(clock.Generation > generation, "generation");
clock.Seek(-1);
Near(clock.PositionMs, 0, "seek lower clamp");
clock.Seek(20000);
Near(clock.PositionMs, 10000, "seek upper clamp");
var song = new Song { Lines = [new() { Text = "a", StartMs = 1000, EndMs = 3000 }, new() { Text = "b", StartMs = 2000, EndMs = 4000 }, new() { Text = "c", StartMs = 9000, EndMs = 10000 }] };
var grouped = Timeline.Group(song, true, false);
Check(Timeline.Anchor(grouped, 999) == -1, "before");
Check(Timeline.Anchor(grouped, 2500) == 0, "overlap anchor");
Check(Timeline.Active(grouped[0], 2500) && Timeline.Active(grouped[1], 2500), "multiple active");
Check(!Timeline.Active(grouped[0], 1000), "strict start");
Check(Timeline.Anchor(grouped, 11000) == 3, "end sentinel");
song.Lines[2].EndMs = 0;
Check(Timeline.Anchor(grouped, 11000) == 2, "unknown end");
var duet = new Song { Lines = [new() { Text = "a", StartMs = 1000, EndMs = 2000, AlignLeft = true }, new() { Text = "(bg)", StartMs = 1100, EndMs = 1800, Background = true }, new() { Text = "b", StartMs = 3000, EndMs = 4000, AlignLeft = false }] };
var rows = Timeline.Group(duet, true, false);
Check(rows.Count == 2 && rows[0].Backgrounds.Count == 1 && rows[1].Right, "duet backgrounds");
var bracket = new LyricLine { Text = "(plain)" };
Check(!bracket.IsBackground, "untimed bracket");
bracket.Words.Add(new() { Text = "plain", StartMs = 0, DurationMs = 1000 });
Check(bracket.IsBackground, "timed bracket");
bracket.Background = false;
Check(!bracket.IsBackground, "explicit background false");
var gapSong = new Song { Lines = [new() { Text = "a", StartMs = 6000, EndMs = 7000 }, new() { Text = "b", StartMs = 13000, EndMs = 14000 }] };
Check(Timeline.BreathingGap(Timeline.Group(gapSong, false, false), 1000) is not null, "intro dots");
Check(Timeline.BreathingGap(Timeline.Group(gapSong, false, false), 8000) is null, "exact 6000 gap");
gapSong.Lines[1].StartMs = 13001;
Check(Timeline.BreathingGap(Timeline.Group(gapSong, false, false), 8000) is not null, "long gap");
var word = new LyricWord { Text = "hello", StartMs = 1000, DurationMs = 1000, Syllables = [new() { Text = "he", StartMs = 1000, DurationMs = 400 }, new() { Text = "llo", StartMs = 1600, DurationMs = 400 }] };
Near(Timeline.Fraction(word, 1500), .4, "syllable hold");
Near(Timeline.Fraction(word, 1800), .7, "syllable progress");
Near(Timeline.Fraction(word, 1100), .1, "syllable rewind");
Near(Timeline.Fraction(word, 500), 0, "syllable before");
word.DurationMs = 1500;
Check(Timeline.Emphasis(word, false, MotionPreset.Ios264), "emphasis");
word.DurationMs = 1000;
Check(!Timeline.Emphasis(word, false, MotionPreset.Ios264), "strict emphasis");
var cjk = new LyricWord { Text = "中", DurationMs = 3000 };
Check(!Timeline.Emphasis(cjk, true, MotionPreset.Ios264), "single CJK");
var seven = new LyricWord { Text = "abcdefg", DurationMs = 2000 };
Check(!Timeline.Emphasis(seven, false, MotionPreset.Ios264) && Timeline.Emphasis(seven, true, MotionPreset.Ios264), "last emphasis");
Near(Motion.Critical(.25, 1.5, 1.5 * 1.4695433963885915), .671067519285765, "critical original");
Near(Motion.Scroll(.25), .674037, "scroll original");
var mask = Motion.Mask(.1, 200, 1);
Near(mask.First, .11944444444444445, "mask first");
Near(mask.Second, .1736111111111111, "mask second");
TextMeasure Measure(string text) => new(text.Length * 10, text.TrimEnd().Length * 10, 50);
var wrap = LineBreaker.Wrap([new() { Text = "i " }, new() { Text = "hel" }, new() { Text = "lo" }], 50.01, Measure);
Check(wrap.Count == 2 && wrap[0].Words.Count == 1 && wrap[1].Words.Count == 2, "word fragment carry");
wrap = LineBreaker.Wrap([new() { Text = "你" }, new() { Text = "好" }, new() { Text = "世" }, new() { Text = "界" }], 20.01, Measure);
Check(wrap.Count == 2 && wrap[0].Words.Count == 2, "CJK wrap");
wrap = LineBreaker.Wrap([new() { Text = "hel" }, new() { Text = "lo" }], 30.01, Measure);
Check(wrap.Count == 1, "unbreakable token");
Check(double.IsFinite(LineBreaker.GlyphOrigin(5, 100, 50, 20, 20)), "glyph origin");
var lrc = LyricsFile.ParseLrc("[ti:Fixture]\n[ar:Test]\n[00:01.00]Hello\n[00:03.50]World");
Check(lrc.Title == "Fixture" && lrc.Lines.Count == 2, "LRC");
Near(lrc.Lines[0].EndMs, 3500, "LRC end");
var enhanced = LyricsFile.ParseLrc("[00:01.00]<00:01.00>he<00:01.40>llo<00:02.00>\n[00:03.00]next");
Check(enhanced.Lines[0].Words.Count == 2, "enhanced LRC");
Near(enhanced.Lines[0].Words[0].DurationMs, 400, "enhanced duration");
var ttml = LyricsFile.ParseTtml("<tt xmlns='http://www.w3.org/ns/ttml'><body><div><p begin='1s' end='3s'><span begin='1s' end='2s'>hello </span><span begin='2s' end='3s'>world</span></p></div></body></tt>");
Check(ttml.Lines.Count == 1 && ttml.Lines[0].Words.Count == 2, "TTML");
bool dtdRejected = false;
try { LyricsFile.ParseTtml("<!DOCTYPE tt [<!ENTITY x SYSTEM 'file:///test'>]><tt>&x;</tt>"); } catch (System.Xml.XmlException) { dtdRejected = true; }
Check(dtdRejected, "DTD rejected");
var pickup = new Song { Lines = [new() { Text = "main", StartMs = 2000, EndMs = 3000 }, new() { Text = "bg", StartMs = 1500, EndMs = 2500, Background = true }] };
pickup.Validate();
var pickupRows = Timeline.Group(pickup, false, false);
Check(pickupRows.Count == 1 && pickupRows[0].StartMs == 1500, "background pickup keeps parent");
Check(LyricsFile.ParseLrc("plain line\nsecond line").Lines.Count == 2, "plain lyrics");
var plainRows = Timeline.Group(LyricsFile.ParseLrc("plain line\nsecond line"), false, false);
Check(Timeline.Anchor(plainRows, 3000) == -1 && !Timeline.Active(plainRows[0], 3000), "plain lyrics do not auto-select");
bool invalidRejected = false;
try { new Song { Lines = null! }.Validate(); } catch (FormatException) { invalidRejected = true; }
Check(invalidRejected, "null input rejected");
var nativePlans = System.Text.Json.JsonSerializer.Deserialize<MarqueeTiming[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "marquee-original.json")))!;
foreach (var original in nativePlans)
{
    var rebuilt = MarqueeTiming.Create(original.Width, original.Delay);
    Near(rebuilt.From, original.From, "marquee start original");
    Near(rebuilt.To, original.To, "marquee end original");
    Near(rebuilt.EnterPosition, original.EnterPosition, "marquee ease-in original");
    Near(rebuilt.CruisePosition, original.CruisePosition, "marquee cruise original");
    Near(rebuilt.EnterEnd, original.EnterEnd, "marquee enter time original");
    Near(rebuilt.CruiseEnd, original.CruiseEnd, "marquee cruise time original");
    Near(rebuilt.End, original.End, "marquee duration original");
}
Check(MagicGesture.Select(0, 0, false, false) == MagicAction.Close, "magic primary click");
Check(MagicGesture.Select(49.999, 0, false, false) == MagicAction.Close, "magic below horizontal threshold");
Check(MagicGesture.Select(50, 0, false, false) == MagicAction.Maximize, "magic horizontal threshold");
Check(MagicGesture.Select(-50, 0, false, true) == MagicAction.Restore, "magic maximized restore");
Check(MagicGesture.Select(0, -4.999, false, false) == MagicAction.Close, "magic below cancel threshold");
Check(MagicGesture.Select(0, -5, false, false) == MagicAction.Cancel, "magic cancel threshold");
Check(MagicGesture.Select(0, 50, false, false) == MagicAction.Minimize, "magic downward minimize");
Check(MagicGesture.Select(50, 50, false, false) == MagicAction.Maximize, "magic diagonal gives horizontal priority");
Check(MagicGesture.Select(0, 0, true, false) == MagicAction.None, "magic secondary click");
Check(MagicGesture.Select(0, -50, true, false) == MagicAction.Top && MagicGesture.Select(0, 50, true, false) == MagicAction.Bottom, "magic secondary vertical navigation");
Check(MagicGesture.Select(50, 50, true, false) == MagicAction.Current, "magic secondary current line");
Check(!BreathingMotion.At(1999, 1000, 13000, true).Visible, "breathing entry delay");
var breathing = BreathingMotion.At(2800, 1000, 13000, true);
Check(breathing.Visible && breathing.First >= breathing.Second && breathing.Second > breathing.Third, "breathing staggered entry");
Near(breathing.First, .15, "breathing initial opacity original");
Near(BreathingMotion.At(5504, 1000, 13000, true).First, 1, "breathing first full brightness original");
Check(BreathingMotion.At(5504, 1000, 13000, true).Second < 1, "breathing next dot remains dim");
Check(BreathingMotion.At(11740, 1000, 13000, true).Scale < BreathingMotion.At(12640, 1000, 13000, true).Scale, "breathing exit expansion");
Check(BreathingMotion.At(12850, 1000, 13000, true).Opacity < 1, "breathing exit fade");
Check(!BreathingMotion.At(13001, 1000, 13000, true).Visible, "breathing after gap");
Near(BreathingMotion.At(5504, 1000, 13000, true, true).Scale, 1, "reduced breathing motion");
using (var originalMotion = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "line-motion-original.json"))))
{
    foreach (var curve in originalMotion.RootElement.GetProperty("curves").EnumerateArray())
    {
        bool showing = curve.GetProperty("showing").GetBoolean();
        Near(BackgroundSpring.Duration(showing), curve.GetProperty("duration").GetDouble(), "background settling original");
        foreach (var point in curve.GetProperty("points").EnumerateArray())
            Near(BackgroundSpring.Ease(showing, point.GetProperty("p").GetDouble()), point.GetProperty("value").GetDouble(), "background easing original");
    }
    foreach (var sample in originalMotion.RootElement.GetProperty("scales").EnumerateArray())
        Near(LineMotion.At(5000, 1000, 10000, sample.GetProperty("timed").GetBoolean(), false, false).ContentScale, sample.GetProperty("scale").GetDouble(), "highlight scale original");
    var defaults = originalMotion.RootElement.GetProperty("defaults");
    var options = new ViewOptions();
    Check(options.Blur == defaults.GetProperty("BlurLyrics").GetBoolean() && options.Bold == defaults.GetProperty("BoldText").GetBoolean(), "default text settings original");
    Near(options.BlurLevel, defaults.GetProperty("BlurLevel").GetDouble(), "default blur intensity original");
    Check(defaults.GetProperty("WordShineEffectPreset").GetString() == "iOS162" && options.Preset == MotionPreset.Ios, "default word effect original");
    foreach (var curve in originalMotion.RootElement.GetProperty("floats").EnumerateArray())
        foreach (var point in curve.GetProperty("points").EnumerateArray())
            Near(curve.GetProperty("name").GetString() == "FloatingEasingFunction" ? FloatingMotion.Ease(point.GetProperty("p").GetDouble()) : FloatingMotion.SyllableEase(point.GetProperty("p").GetDouble()), point.GetProperty("value").GetDouble(), "floating curve original");
}
using (var originalBackground = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "background-text-original.json"))))
{
    foreach (var sample in originalBackground.RootElement.EnumerateArray())
    {
        var parts = sample.GetProperty("input").EnumerateArray().Select(x => x.GetString()!).ToArray();
        var input = new LyricLine { Text = string.Concat(parts), StartMs = 1000, EndMs = 1000 + 300 * parts.Length, Words = parts.Select((text, index) => new LyricWord { Text = text, StartMs = 1000 + index * 300, DurationMs = 300 }).ToList() };
        Check(input.IsBackground == sample.GetProperty("recognized").GetBoolean(), "background bracket recognition original");
        var display = BackgroundText.ForDisplay(input, false);
        Check(display.Text == sample.GetProperty("text").GetString(), "background display text original");
        var originalWords = sample.GetProperty("words").EnumerateArray().ToArray();
        for (int i = 0; i < originalWords.Length; i++)
        {
            Check(display.Words[i].Text == originalWords[i].GetProperty("text").GetString(), "background token text original");
            Near(display.Words[i].StartMs, originalWords[i].GetProperty("start").GetDouble(), "background token timing retained");
        }
        Check(input.Text == string.Concat(parts) && input.Words.Select(x => x.Text).SequenceEqual(parts), "display adaptation preserves imported data");
        Check(display.AlignLeft == false, "background inherits main alignment");
    }
}
Near(Motion.PullingDelayMs(1), 50, "original first downstream pulling delay");
Near(Motion.PullingDelayMs(3), 150, "original third downstream pulling delay");
var seekPreview = SeekMotion.Preview(100, 200, 10000);
Check(seekPreview.TimeMs == 5000 && seekPreview.Elastic == 0, "seek pointer midpoint");
seekPreview = SeekMotion.Preview(-20, 200, 10000);
Check(seekPreview.TimeMs == 0 && seekPreview.Elastic < 0, "seek pointer left clamp");
Near(seekPreview.Elastic, -Math.Pow(20, .76) * 1.1, "left elastic original");
seekPreview = SeekMotion.Preview(220, 200, 10000);
Check(seekPreview.TimeMs == 10000 && seekPreview.Elastic > 0, "seek pointer right clamp");
Near(seekPreview.Elastic, Math.Pow(20, .76) * 1.1, "right elastic original");
Check(SeekMotion.Preview(100, 200, 0).TimeMs == 0, "unknown duration preview");
var backdrop = LegacyBackdrop.At(0, 0, 180);
Near(backdrop.First.ScaleX, 1.1, "legacy first initial scale");
Near(backdrop.First.X, -160, "legacy first initial position");
Near(backdrop.Second.ScaleY, 4, "legacy second initial scale");
backdrop = LegacyBackdrop.At(25, 0, 180);
Near(backdrop.First.ScaleX, 2.6, "legacy first scale reaches its turning point");
Near(backdrop.First.Y, 170, "legacy first Y reaches its turning point");
backdrop = LegacyBackdrop.At(50, 0, 180);
Near(backdrop.First.ScaleX, 1.1, "legacy first scale returns");
Near(backdrop.First.Y, -160, "legacy first Y returns");
backdrop = LegacyBackdrop.At(90, 0, 180);
Near(backdrop.First.Angle, 360, "legacy first rotation period");
backdrop = LegacyBackdrop.At(70, 0, 180);
Near(backdrop.Second.Angle, -180, "legacy second rotation period");
Near(ZoomRules.AdjustInverse(1.1, true), 1, "zoom snaps near the source neutral level");
Near(ZoomRules.AdjustInverse(1, true), .9, "source zoom in multiplier");
Near(ZoomRules.AdjustInverse(.9, false), 1, "source zoom out multiplier");
Near(ZoomRules.AdjustInverse(.33, true), .33, "source maximum display scale");
Near(ZoomRules.AdjustInverse(2, false), 2, "source minimum display scale");
var staticLineFrame = LineMotion.At(1000, double.PositiveInfinity, 0, false, false, false);
Near(staticLineFrame.PlayedAlpha, .3, "untimed frame remains unselected");
Near(staticLineFrame.ContentScale, 1, "untimed frame remains unscaled");
var copyLine = new LyricLine { Text = "one", Translation = "一" };
var copyOther = new LyricLine { Text = "two" };
var copyLines = new[] { copyLine, copyOther };
Check(CopyText.Build(CopyKind.Line, copyLine, copyLines, copyLines) == "one", "copy line preserves text");
Check(CopyText.Build(CopyKind.LineWithTranslation, copyLine, copyLines, copyLines) == "one" + Environment.NewLine + "一", "copy line translation format original");
Check(CopyText.Build(CopyKind.AllLines, copyLine, copyLines, copyLines) == "one" + Environment.NewLine + "two", "copy all lines format original");
Check(CopyText.Build(CopyKind.AllTranslations, copyLine, copyLines, copyLines) == "一", "copy all translations skips blanks");
Check(CopyText.Build(CopyKind.AllWithTranslations, copyLine, copyLines, copyLines) == "one" + Environment.NewLine + "一" + Environment.NewLine + Environment.NewLine + "two", "combined copy uses original paragraph spacing");
var glowWord = new LyricWord { Text = "word", StartMs = 1000, DurationMs = 1200 };
Near(Motion.WordGlow(1149, glowWord), 0, "glow initial delay original");
Near(Motion.WordGlow(1650, glowWord), .43 * .7 / 1.2 * .5 / .7, "short word glow interrupts the rise at its current value");
Near(Motion.WordGlow(2350, glowWord), 0, "short word glow ends at word end plus delay");
Check(FloatingMotion.Y(1200, 2000, 1000, 1000, 41) > 0, "future word is prepared below the baseline");
Check(FloatingMotion.Y(5000, 2000, 1000, 1000, 41) < 0, "sung word reaches the lifted baseline");
var legacyWord = new LyricWord { Text = "hi", StartMs = 1000, DurationMs = 2000 };
var legacyPose = Motion.Glyph(1600, legacyWord, 0, 2, MotionPreset.Lyricify, 41, 50, true);
Check(legacyPose.Scale > 1 && legacyPose.Scale < 1.08 && legacyPose.Y < 0, "legacy two-character target follows its source parameters");
legacyPose = Motion.Glyph(10000, legacyWord, 0, 2, MotionPreset.Lyricify, 41, 50, true);
Near(legacyPose.Scale, 1, "legacy character returns to neutral");
Near(Motion.Glyph(1049, legacyWord, 0, 2, MotionPreset.Lyricify, 41, 50, true).Scale, 1, "legacy first character waits for its capped 50 ms delay");
Near(Motion.Glyph(1100, legacyWord, 0, 2, MotionPreset.Lyricify, 41, 50, true).Scale, 1.0002430555555555, "legacy first character grows from its recovered start time");
Near(Motion.Glyph(1420, legacyWord, 1, 2, MotionPreset.Lyricify, 41, 50, true).Scale, 1, "legacy second character includes the preceding integer waits");
Check(Motion.Glyph(1500, legacyWord, 1, 2, MotionPreset.Lyricify, 41, 50, true).Scale > 1, "legacy plain second character has started at 500 ms");
var legacySyllables = new LyricWord { Text = "hi", StartMs = 1000, DurationMs = 2000, Syllables = [new() { Text = "h", StartMs = 1000, DurationMs = 300 }, new() { Text = "i", StartMs = 1300, DurationMs = 1700 }] };
Near(Motion.Glyph(1500, legacySyllables, 1, 2, MotionPreset.Lyricify, 41, 50, true).Scale, 1, "legacy syllable timing delays the second character until 550 ms");
Check(Motion.Glyph(1600, legacySyllables, 1, 2, MotionPreset.Lyricify, 41, 50, true).Scale > 1, "legacy syllable-delayed character eventually starts");
var leadingSyllables = new LyricWord { Text = " hi", StartMs = 1000, DurationMs = 2000, Syllables = [new() { Text = " ", StartMs = 1000, DurationMs = 100 }, new() { Text = "h", StartMs = 1100, DurationMs = 300 }, new() { Text = "i", StartMs = 1400, DurationMs = 1700 }] };
Near(Motion.Glyph(1600, leadingSyllables, 1, 2, MotionPreset.Lyricify, 41, 50, true).Scale, Motion.Glyph(1600, legacySyllables, 1, 2, MotionPreset.Lyricify, 41, 50, true).Scale, "legacy subitem lookup counts the leading space without animating it");
legacySyllables.Syllables.Insert(0, new() { Text = "", StartMs = 1000, DurationMs = 0 });
Check(double.IsFinite(Motion.Glyph(1600, legacySyllables, 1, 2, MotionPreset.Lyricify, 41, 50, true).Scale), "empty legacy syllables cannot divide by zero");
var gapSyllables = new LyricWord { Text = "hey", StartMs = 1000, DurationMs = 2000, Syllables = [new() { Text = "h", StartMs = 1000, DurationMs = 100 }, new() { Text = "e", StartMs = 1300, DurationMs = 200 }, new() { Text = "y", StartMs = 1500, DurationMs = 1500 }] };
Near(Motion.Glyph(1188, gapSyllables, 1, 3, MotionPreset.Lyricify, 41, 50, true).Scale, 1, "legacy subitem gaps contribute to the next character delay");
Check(Motion.Glyph(1198, gapSyllables, 1, 3, MotionPreset.Lyricify, 41, 50, true).Scale > 1, "legacy character starts after the gap-adjusted 188 ms wait");
gapSyllables.Syllables[0].DurationMs = 1;
gapSyllables.Syllables[1].StartMs = 1001;
gapSyllables.Syllables[1].DurationMs = 1;
gapSyllables.Syllables[2].StartMs = 1002;
Near(Motion.Glyph(1094, gapSyllables, 1, 3, MotionPreset.Lyricify, 41, 50, true).Scale, 1, "legacy per-subitem step has an 80 ms minimum");
Check(Motion.Glyph(1104, gapSyllables, 1, 3, MotionPreset.Lyricify, 41, 50, true).Scale > 1, "legacy minimum-step character starts after 94 ms");
var longOverlap = Timeline.Group(new Song { Lines = [new() { Text = "long", StartMs = 1000, EndMs = 10000 }, new() { Text = "short", StartMs = 2000, EndMs = 3000 }] }, false, false);
using (var nativePreparation = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lyrics-preparation-original.json"))))
{
    foreach (var sample in nativePreparation.RootElement.GetProperty("cases").EnumerateArray())
    {
        var input = new Song();
        foreach (var row in sample.GetProperty("input").EnumerateArray())
        {
            string text = row.GetProperty("text").GetString()!;
            double start = row.GetProperty("start").GetDouble(), end = row.GetProperty("end").GetDouble();
            var background = row.GetProperty("background");
            input.Lines.Add(new LyricLine { Text = text, StartMs = start, EndMs = end, Background = background.ValueKind == System.Text.Json.JsonValueKind.Null ? null : background.GetBoolean(), Words = sample.GetProperty("timed").GetBoolean() ? [new() { Text = text, StartMs = start, DurationMs = end - start }] : [] });
        }
        string before = System.Text.Json.JsonSerializer.Serialize(input);
        var prepared = LyricsPreparation.ForPage(input, true, false);
        var expected = sample.GetProperty("groups");
        Check(prepared.Count == expected.GetArrayLength(), "native preparation group count: " + sample.GetProperty("name").GetString());
        for (int i = 0; i < prepared.Count; i++)
        {
            var actual = prepared[i];
            var original = expected[i];
            Check(actual.Main.Text == original.GetProperty("text").GetString(), "native preparation preserves source row order");
            Near(actual.StartMs, original.GetProperty("start").GetDouble(), "native prepared display start");
            Near(actual.EndMs, original.GetProperty("end").GetDouble(), "native prepared display end");
            var background = original.GetProperty("background");
            Check(background.ValueKind == System.Text.Json.JsonValueKind.Null ? actual.Backgrounds.Count == 0 : actual.Backgrounds.Count == 1 && actual.Backgrounds[0].Text == background.GetString()!.Trim('(', ')', '（', '）').Trim(), "native preparation consumes exactly one classified backing row");
        }
        Check(System.Text.Json.JsonSerializer.Serialize(input) == before, "native preparation does not modify imported lyrics");
    }
    var emptyPreparation = LyricsPreparation.ForPage(new Song(), false, false);
    Check(emptyPreparation.Count == 0, "empty preparation remains empty");
}
Check(Timeline.Anchor(longOverlap, 4000) == 0, "completed last row does not end an earlier overlap");
Check(Timeline.Anchor(longOverlap, 11000) == 2, "overlap ends only when its anchor reaches the final sentinel");
bool hugeRejected = false;
try { new Song { DurationMs = 1e100 }.Validate(); } catch (FormatException) { hugeRejected = true; }
Check(hugeRejected, "huge duration is rejected before UI integer conversion");
bool sumRejected = false;
try { new Song { Lines = [new() { Words = [new() { StartMs = 86400000, DurationMs = 1 }] }] }.Validate(); } catch (FormatException) { sumRejected = true; }
Check(sumRejected, "summed word end remains in the supported time range");
var daySong = new Song { Lines = [new() { Text = "end", EndMs = 86400000 }] };
daySong.Validate();
Near(daySong.DurationMs, 86400000, "inferred tail remains within the time range");
var invalidOptions = new ViewOptions { Preset = (MotionPreset)99, Background = (BackgroundMode)99 };
invalidOptions.Normalize();
Check(invalidOptions.Preset == MotionPreset.Ios && invalidOptions.Background == BackgroundMode.Static, "invalid persisted modes return to reference defaults");
bool depthRejected = false;
try { LyricsFile.ParseTtml("<tt>" + string.Concat(Enumerable.Repeat("<span begin='1s'>", 100)) + "text" + string.Concat(Enumerable.Repeat("</span>", 100)) + "</tt>"); } catch (FormatException) { depthRejected = true; }
Check(depthRejected, "deep TTML is rejected before recursive span parsing");
string networkFixture = Path.Combine(AppContext.BaseDirectory, "network-path-fixture.json");
try
{
    File.WriteAllText(networkFixture, System.Text.Json.JsonSerializer.Serialize(new
    {
        AudioPath = @"\\invalid.example\share\audio.mp3",
        Lines = Array.Empty<LyricLine>()
    }));
    bool networkRejected = false;
    try
    {
        LyricsFile.Load(networkFixture);
    }
    catch (FormatException) { networkRejected = true; }
    Check(networkRejected, "embedded UNC paths are rejected before media access");
}
finally { File.Delete(networkFixture); }
using (var originalTimer = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "timer-original.json"))))
{
    var plan = new TimerPlan();
    foreach (var sample in originalTimer.RootElement.GetProperty("cases").EnumerateArray())
    {
        var slots = plan.Update(sample.GetProperty("timeMs").GetDouble());
        var originals = sample.GetProperty("slots").EnumerateArray().ToArray();
        for (int i = 0; i < 6; i++)
        {
            Check(slots[i].Value.ToString() == originals[i].GetProperty("text").GetString(), "timer glyph original");
            Near(slots[i].DelayMs, originals[i].GetProperty("delayMs").GetDouble(), "timer stagger original");
            Check(slots[i].Visible == originals[i].GetProperty("visible").GetBoolean(), "timer visibility original");
        }
    }
}
Near(LineMotion.At(0, 1000, 10000, true, false, false).PlayedAlpha, 47d / 255, "unplayed timed line alpha original");
Near(LineMotion.At(0, 1000, 10000, true, false, false).SecondaryAlpha, 55d / 255, "future translation alpha original");
Near(LineMotion.At(5000, 1000, 10000, true, false, false).UnplayedAlpha, 71d / 255, "current word preplay alpha original");
Near(LineMotion.At(5000, 1000, 10000, true, false, false).SecondaryAlpha, .7, "current translation alpha original");
Near(LineMotion.At(5000, 1000, 10000, true, true, false).RootOpacity, .66, "active backing alpha original");
Near(LineMotion.At(12000, 1000, 10000, true, true, false).RootOpacity, 0, "backing leaves after parent");
Near(LineMotion.At(0, 1000, 10000, true, true, false).RootScale, .9, "backing initial scale original");
double rowNow = 0;
var rowClock = new PlaybackClock(() => rowNow);
rowClock.Load(20000);
rowClock.Seek(1050);
rowClock.Play();
var initialFrame = LineMotion.At(rowClock.PositionMs, 1000, 10000, true, false, false);
rowClock.Pause();
rowNow = 10000;
Check(LineMotion.At(rowClock.PositionMs, 1000, 10000, true, false, false) == initialFrame, "paused line frame does not advance with wall time");
Check(LineMotion.At(1500, 1000, 10000, true, false, false).ContentScale > LineMotion.At(1050, 1000, 10000, true, false, false).ContentScale, "rewind reconstructs line scale");
Console.WriteLine($"PASS {assertions} assertions");
