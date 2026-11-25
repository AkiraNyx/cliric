using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Cliric.Core;

namespace Cliric.App;

internal static class WordEdgeChecks
{
    public static void Run(Action<bool, string> check, string folder)
    {
        CheckHanStrokeWeight(check);
        var cjkFamily = Cliric.App.Reference.ReferencePageView.FontFor(FontPresets.SpotifyMix);
        var cjkText = new FormattedText("让人难忘", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(cjkFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 41, Brushes.White, 1);
        var cjkRow = new LyricRow(new()
        {
            Text = "让人难忘",
            StartMs = 1000,
            EndMs = 5000,
            Words = [new() { Text = "让人难忘", StartMs = 1000, DurationMs = 4000 }]
        }, false, false, 500, 1, cjkFamily, new ViewOptions());
        cjkRow.Measure(new Size(500, double.PositiveInfinity));
        cjkRow.Arrange(new Rect(0, 0, 500, cjkRow.DesiredSize.Height));
        cjkRow.UpdateLayout();
        var cjkBox = new TextBlock { Text = "让人难忘", FontFamily = cjkFamily, FontSize = 41, FontWeight = FontWeights.Bold, MinHeight = 50 };
        cjkBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var originalHost = new UserControl { Padding = new Thickness(0, 0, 0, -4), Content = cjkBox };
        originalHost.Measure(new Size(500, double.PositiveInfinity));
        double expectedCjkHeight = originalHost.DesiredSize.Height + 30;
        check(Math.Abs(cjkRow.NaturalHeight - expectedCjkHeight) < 1e-6, "original TextSing uses its real CJK line box and UserControl padding semantics: actual=" + cjkRow.NaturalHeight + " expected=" + expectedCjkHeight + " fontHeight=" + cjkText.Height + " blockHeight=" + cjkBox.DesiredSize.Height);
        var clockLine = new LyricLine { Text = "hello world", StartMs = 1000, EndMs = 5000, Words = [new() { Text = "hello ", StartMs = 1000, DurationMs = 2000 }, new() { Text = "world", StartMs = 3000, DurationMs = 1000 }] };
        var clockOptions = new ViewOptions { Float = false, WordShine = false };
        LyricRow ClockRow(double width = 500)
        {
            var result = new LyricRow(clockLine, false, false, width, 1, new FontFamily("Segoe UI"), clockOptions);
            result.Measure(new Size(width, double.PositiveInfinity));
            result.Arrange(new Rect(0, 0, width, result.DesiredSize.Height));
            result.UpdateLayout();
            return result;
        }
        static double MaskOffset(LyricRow row) => ((LinearGradientBrush)Find(row).OfType<Canvas>().First(canvas => canvas.OpacityMask is LinearGradientBrush).OpacityMask).GradientStops[0].Offset;
        var clockRow = ClockRow();
        clockRow.Update(1500, 1500, true, 0, 1000, 5000, 1, 20000);
        double beforePause = MaskOffset(clockRow);
        clockRow.Update(1500, 1500, true, 0, 1000, 5000, 1, 20500);
        double pausedSweep = MaskOffset(clockRow);
        check(pausedSweep > beforePause && clockRow.RunningWord == 0, "paused playback retains word selection while the dispatched mask advances on its animation clock");
        var clockReflow = ClockRow(520);
        clockReflow.RestoreWordRun(clockRow);
        clockReflow.Update(1500, 1500, true, 0, 1000, 5000, 1, 20750);
        check(MaskOffset(clockReflow) > pausedSweep && clockReflow.RunningWord == 0, "same-count reflow preserves the running wall-clock mask while playback is paused");
        clockRow.Update(1500, 1500, true, 0, 1000, 5000, 1, 26000);
        double completedFirst = MaskOffset(clockRow);
        check(clockRow.RunningWord == 0 && completedFirst < .9, "a completed mask cannot select or highlight a future word while paused");
        clockRow.Update(3100, 3100, true, 0, 1000, 5000, 1, 27000);
        check(clockRow.RunningWord == 1 && Math.Abs(MaskOffset(clockRow) - completedFirst) < 1e-9, "resuming dispatches the next word from the actual finished mask snapshot");
        clockRow.Update(3100, 3100, true, 0, 1000, 5000, 1, 27500);
        check(MaskOffset(clockRow) > completedFirst, "the next mask plan continues on wall time after another pause");
        clockRow.Update(1500, 1500, true, 0, 1000, 5000, 2, 28000);
        check(MaskOffset(clockRow) == 0 && clockRow.RunningWord == 0, "seek replaces the prior wall-clock origin instead of reusing its elapsed time");
        var earlyClock = ClockRow();
        earlyClock.NextLineStartMs = 2000;
        earlyClock.Update(1000, 1000, true, 0, 1000, 5000, 1, 30000);
        earlyClock.Update(1501, 1501, true, 0, 1000, 5000, 1, 30501);
        double earlyStart = MaskOffset(earlyClock);
        earlyClock.Update(1501, 1501, true, 0, 1000, 5000, 1, 30613);
        check(earlyClock.FinishingWords && earlyClock.RunningWord == 0 && MaskOffset(earlyClock) > earlyStart, "forced-finish mask animation continues during pause without advancing word dispatch");
        foreach (var preset in Enum.GetValues<MotionPreset>())
        {
            var options = new ViewOptions { Preset = preset };
            var line = new LyricLine { Text = "ab", StartMs = 1000, EndMs = 5000, Words = [new() { Text = "ab", StartMs = 1000, DurationMs = 4000 }] };
            var row = new LyricRow(line, false, false, 500, 1, new FontFamily("Segoe UI"), options);
            row.Measure(new Size(500, double.PositiveInfinity));
            row.Arrange(new Rect(0, 0, 500, row.DesiredSize.Height));
            row.UpdateLayout();
            var clockGlyph = Find(row).OfType<TextBlock>().Single(text => text.Text == "a" && text.RenderTransform is TransformGroup);
            var transform = (TransformGroup)clockGlyph.RenderTransform;
            var scale = (ScaleTransform)transform.Children[0];
            var move = (TranslateTransform)transform.Children[1];
            row.Update(1000, 1000, true, 0, 1000, 5000, 1, 10000);
            double preparedY = move.Y;
            row.Update(1000, 1000, true, 0, 1000, 5000, 1, 11000);
            check(scale.ScaleX > 1 && Math.Abs(move.Y - preparedY) > .01 && row.RunningWord == 0, "paused emphasis and floating continue on their dispatched animation clock: " + preset);
            double seekFromY = move.Y;
            row.Update(1500, 1500, true, 0, 1000, 5000, 2, 11000);
            check(Math.Abs(move.Y - seekFromY) < 1e-9, "seek prepares the emphasized glyph from its current Y: " + preset);
            row.Update(5001, 5001, false, 1, 1000, 5000, 2, 11500);
            row.Update(5001, 5001, false, 1, 1000, 5000, 2, 12500);
            check(Math.Abs(move.Y) < 1e-9, "outer line Stop returns glyph Y on wall time while playback stays paused: " + preset);
            double heldScale = scale.ScaleX;
            row.Update(1000, 1000, true, 0, 1000, 5000, 3, 13000);
            check(scale.ScaleX == heldScale && row.RunningWord == 0, "seek starts a fresh character delay from the original current-scale snapshot: " + preset);
        }
        var pendingOptions = new ViewOptions { WordShine = false };
        var pendingFloat = new LyricRow(clockLine, false, false, 500, 1, new FontFamily("Segoe UI"), pendingOptions);
        pendingFloat.Measure(new Size(500, double.PositiveInfinity));
        pendingFloat.Arrange(new Rect(0, 0, 500, pendingFloat.DesiredSize.Height));
        pendingFloat.UpdateLayout();
        pendingFloat.Update(1000, 1000, true, 0, 1000, 5000, 1, 10000);
        pendingFloat.Update(1000, 1000, true, 0, 1000, 5000, 1, 15000);
        var pendingText = Find(pendingFloat).OfType<TextBlock>().Single(text => text.Text == "world" && text.RenderTransform is TranslateTransform);
        check(pendingFloat.RunningWord == 0 && ((TranslateTransform)pendingText.RenderTransform).Y > 0, "a future ordinary word stays prepared instead of starting its lift during a long pause");
        var coldFloat = new LyricRow(clockLine, false, false, 500, 1, new FontFamily("Segoe UI"), pendingOptions);
        coldFloat.Measure(new Size(500, double.PositiveInfinity));
        coldFloat.Arrange(new Rect(0, 0, 500, coldFloat.DesiredSize.Height));
        coldFloat.UpdateLayout();
        var coldMoves = Find(coldFloat).OfType<TextBlock>()
            .Where(text => text.RenderTransform is TranslateTransform && (text.Text is "hello " or "world"))
            .Select(text => (TranslateTransform)text.RenderTransform).ToArray();
        coldFloat.Update(1500, 1500, true, 0, 1000, 5000, 1, 10000);
        check(coldMoves.Length == 2 && coldMoves.All(move => move.Y == 0), "cold midword entry lifts from the rendered baseline at dispatch");
        coldFloat.Update(1500, 1500, true, 0, 1000, 5000, 1, 10250);
        check(coldMoves[0].Y < 0 && coldMoves[1].Y > 0, "the dispatched and pending ordinary words follow their separate wall clocks");
        CheckGlowLifetime(check);
        int configurations = 0, stripCount = 0, greatestLoss = 0;
        foreach (int count in new[] { 1, 5 })
            foreach (double scale in new[] { .6, 1, 1.8 })
                foreach (bool portrait in new[] { false, true })
                    foreach (int preset in new[] { 0, 1, 2 })
                        foreach (bool right in new[] { false, true })
                        {
                            configurations++;
                            string[] text = count == 1 ? ["长"] : ["我", "躲", "在", "尾", "长"];
                            var line = new LyricLine { Text = string.Concat(text), StartMs = 1000, EndMs = 5000 };
                            for (int i = 0; i < text.Length; i++)
                                line.Words.Add(new()
                                {
                                    Text = text[i],
                                    StartMs = 1000 + i * 500,
                                    DurationMs = i == text.Length - 1 ? 1000 : 500
                                });
                            var options = new ViewOptions { ReduceMotion = true, Float = false, WordShine = false };
                            options.AppleMusicLyrics.KaraokeStyleProgressWidthPreset = preset;
                            double width = 160 * scale;
                            var row = new LyricRow(line, right, false, width, scale, new FontFamily("Segoe UI"), options, portrait: portrait);
                            row.Measure(new Size(width, double.PositiveInfinity));
                            row.Arrange(new Rect(0, 0, width, row.DesiredSize.Height));
                            row.UpdateLayout();
                            double end = line.Words[^1].EndMs;
                            row.Update(end, end, true, 0, 0, 8000);
                            var strips = Find(row).OfType<Canvas>().Where(c => c.OpacityMask is LinearGradientBrush).ToArray();
                            check(count == 1 ? strips.Length == 1 : strips.Length > 1, "edge fixtures include single glyph and wrapped lines");
                            foreach (var strip in strips)
                            {
                                stripCount++;
                                row.Update(end, end, true, 0, 0, 8000);
                                var brush = strip.OpacityMask;
                                var sung = Capture(strip);
                                strip.OpacityMask = null;
                                var plain = Capture(strip);
                                strip.OpacityMask = brush;
                                byte[] sungPixels = Pixels(sung), plainPixels = Pixels(plain);
                                int maximumLoss = 0, solidPixels = 0;
                                for (int i = 3; i < plainPixels.Length; i += 4)
                                    if (plainPixels[i] > 250)
                                    {
                                        solidPixels++;
                                        maximumLoss = Math.Max(maximumLoss, plainPixels[i] - sungPixels[i]);
                                    }
                                if (scale == 1 && !portrait && preset == 0 && !right)
                                    File.WriteAllBytes(Path.Combine(folder, $"word-tail-{count}-{Array.IndexOf(strips, strip)}.png"), Artwork.Encode(sung));
                                check(solidPixels > 50, "tail coverage fixture contains solid glyph pixels");
                                greatestLoss = Math.Max(greatestLoss, maximumLoss);
                                check(maximumLoss <= 1, $"tail glyph finishes at full opacity; count={count}, scale={scale}, preset={preset}, portrait={portrait}, right={right}, alpha loss={maximumLoss}");
                                row.Update(line.Words[0].StartMs, line.Words[0].StartMs, true, 0, 0, 8000);
                                var first = Capture(strip);
                                byte[] firstPixels = Pixels(first);
                                int earlyWhite = 0;
                                for (int i = 3; i < plainPixels.Length; i += 4)
                                if (plainPixels[i] > 250 && firstPixels[i] > 75)
                                    earlyWhite++;
                                check(earlyWhite == 0, "first glyph starts fully in the unplayed state");
                            }
                        }
        var tailLine = new LyricLine { Text = "hello", StartMs = 1000, EndMs = 3300, Words = [new() { Text = "hello", StartMs = 1000, DurationMs = 2300 }] };
        var motionOptions = new ViewOptions();
        motionOptions.AppleMusicLyrics.KaraokeStyleFloatHeight = 8;
        var animated = new LyricRow(tailLine, false, false, 500, 1, new FontFamily("Segoe UI"), motionOptions);
        animated.Measure(new Size(500, double.PositiveInfinity));
        animated.Arrange(new Rect(0, 0, 500, animated.DesiredSize.Height));
        animated.UpdateLayout();
        var glyph = Find(animated).OfType<TextBlock>().Single(t => t.Text == "h" && t.RenderTransform is TransformGroup);
        var glyphScale = (ScaleTransform)((TransformGroup)glyph.RenderTransform).Children[0];
        var glyphMove = (TranslateTransform)((TransformGroup)glyph.RenderTransform).Children[1];
        animated.Update(1000, 1000, true, 0, 0, 2100);
        animated.Update(2100, 2100, true, 0, 0, 2100);
        double activeScale = glyphScale.ScaleX, activeY = glyphMove.Y;
        animated.Update(2100.01, 2100.01, false, 0, 0, 2100);
        check(activeScale > 1.01 && Math.Abs(glyphScale.ScaleX - activeScale) < .0001 && Math.Abs(glyphMove.Y - activeY) < .0001, "line end starts glyph settling without an immediate transform reset");
        animated.Update(10000, 10000, false, 0, 0, 2100);
        check(Math.Abs(glyphScale.ScaleX - 1) < 1e-9 && Math.Abs(glyphMove.Y) < 1e-9, "outer TextSing stop returns the settled iOS tail to the zero baseline");
        animated.Update(1000, 1000, true, 0, 0, 2100);
        animated.Update(1900, 1900, true, 0, 0, 2100);
        double rewoundScale = glyphScale.ScaleX;
        animated.Update(10000, 10000, false, 0, 0, 2100);
        animated.Update(1000, 1000, true, 0, 0, 2100);
        animated.Update(1900, 1900, true, 0, 0, 2100);
        check(rewoundScale > 1 && glyphScale.ScaleX == rewoundScale, "tail settling can rewind to the same active glyph pose");
        motionOptions.AppleMusicLyrics.BuggyApple = true;
        motionOptions.AppleMusicLyrics.BuggyAppleOptions.WordShineAnimationInterruption = true;
        animated.Update(2100.01, 2100.01, false, 0, 0, 2100);
        check(glyphScale.ScaleX == 1 && Math.Abs(glyphMove.Y - (-4 * (1 - .01 / 607))) < 1e-9, "immediate word interruption is followed by the outer line's baseline return");
        check(glyph.CacheMode is null, "default emphasized glyph uses the original uncached branch");
        var prepared = new LyricRow(tailLine, false, false, 500, 1, new FontFamily("Segoe UI"), new ViewOptions { AppleMusicLyrics = new() { KaraokeStyleFloatHeight = 8 } });
        prepared.Measure(new Size(500, double.PositiveInfinity));
        prepared.Arrange(new Rect(0, 0, 500, prepared.DesiredSize.Height));
        prepared.UpdateLayout();
        var firstPrepared = Find(prepared).OfType<TextBlock>().Single(text => text.Text == "h" && text.RenderTransform is TransformGroup);
        var preparedScale = (ScaleTransform)((TransformGroup)firstPrepared.RenderTransform).Children[0];
        var preparedMove = (TranslateTransform)((TransformGroup)firstPrepared.RenderTransform).Children[1];
        preparedScale.ScaleX = preparedScale.ScaleY = 1.08;
        prepared.Update(900, 900, true, 0, 0, 5000, 1);
        check(Math.Abs(preparedMove.Y - 4) < 1e-9 && preparedScale.ScaleX == 1.08, "emphasized first glyph keeps its current scale and participates in the original positive preparation lift");
        prepared.Update(1000, 1000, true, 0, 0, 5000, 1);
        prepared.Update(1184, 1184, true, 0, 0, 5000, 1);
        check(Math.Abs(preparedMove.Y - 4) < 1e-9 && Math.Abs(preparedScale.ScaleX - 1.08) < 1e-9, "iOS character spring starts from its prepared position and existing scale");
        var futureOptions = new ViewOptions { AppleMusicLyrics = new() { KaraokeStyleFloatHeight = 8, LineSwitchingAppleIosStyle164 = false } };
        var futureLine = new LyricLine { Text = "hi world", StartMs = 1000, EndMs = 4000, Words = [new() { Text = "hi ", StartMs = 1000, DurationMs = 200 }, new() { Text = "world", StartMs = 1800, DurationMs = 1400 }] };
        var future = new LyricRow(futureLine, false, false, 500, 1, new FontFamily("Segoe UI"), futureOptions) { NextLineStartMs = 2000 };
        future.Measure(new Size(500, double.PositiveInfinity));
        future.Arrange(new Rect(0, 0, 500, future.DesiredSize.Height));
        future.UpdateLayout();
        var futureGlyph = Find(future).OfType<TextBlock>().Single(text => text.Text == "w" && text.RenderTransform is TransformGroup);
        var futureScale = (ScaleTransform)((TransformGroup)futureGlyph.RenderTransform).Children[0];
        var futureMove = (TranslateTransform)((TransformGroup)futureGlyph.RenderTransform).Children[1];
        futureScale.ScaleX = futureScale.ScaleY = 1.1;
        future.Update(1000, 1000, true, 0, 0, 1700, 1);
        future.Update(1600, 1600, true, 0, 0, 1700, 1);
        future.Update(2000, 2000, false, 0, 0, 1700, 1);
        double futureYAtStop = FloatingMotion.EarlyReturn(100, 4, 41, 4);
        check(Math.Abs(futureScale.ScaleX - 1.1) < 1e-9 && Math.Abs(futureMove.Y - futureYAtStop * (1 - 300d / 607)) < 1e-6, "outer line stop overrides a never-started shine's early lift with its baseline return");
        futureOptions.AppleMusicLyrics.BuggyApple = true;
        futureOptions.AppleMusicLyrics.BuggyAppleOptions.WordShineAnimationInterruption = true;
        future.Update(2000.01, 2000.01, false, 0, 0, 1700, 1);
        check(futureScale.ScaleX == 1 && Math.Abs(futureMove.Y - (-4 * (1 - 300.01 / 607))) < 1e-9, "outer baseline return also follows the immediate inactive word reset");
        var legacyOptions = new ViewOptions { Preset = MotionPreset.Cliric };
        var legacyLine = new LyricLine { Text = "ab", StartMs = 1000, EndMs = 3000, Words = [new() { Text = "ab", StartMs = 1000, DurationMs = 2000 }] };
        var legacy = new LyricRow(legacyLine, false, false, 500, 1, new FontFamily("Segoe UI"), legacyOptions);
        legacy.Measure(new Size(500, double.PositiveInfinity));
        legacy.Arrange(new Rect(0, 0, 500, legacy.DesiredSize.Height));
        legacy.UpdateLayout();
        legacy.Update(1000, 1000, true, 0, 0, 1250, 1);
        legacy.Update(1249, 1249, true, 0, 0, 1250, 1);
        legacy.Update(1850, 1850, false, 0, 0, 1250, 1);
        var pendingLegacy = Find(legacy).OfType<TextBlock>().Single(text => text.Text == "b" && text.RenderTransform is TransformGroup);
        check(((ScaleTransform)((TransformGroup)pendingLegacy.RenderTransform).Children[0]).ScaleX == 1, "legacy Stop cancels a future character's growth while the first character continues");
        var legacyHost = Find(legacy).OfType<Canvas>().Single(canvas => canvas.Effect is DropShadowEffect);
        check(Math.Abs(((DropShadowEffect)legacyHost.Effect).Opacity - .43) < 1e-9, "legacy Stop retains an already created word glow until its scheduled fade");
        legacy.Update(2550, 2550, false, 0, 0, 1250, 1);
        check(legacyHost.Effect is null, "legacy stopped word releases its glow at the original zero-duration fade");
        var entered = new LyricRow(new()
        {
            Text = "hello world",
            StartMs = 1000,
            EndMs = 4200,
            Words = [new() { Text = "hello ", StartMs = 1000, DurationMs = 1000 }, new() { Text = "world", StartMs = 2200, DurationMs = 2000 }]
        }, false, false, 500, 1, new FontFamily("Segoe UI"), new ViewOptions());
        entered.Measure(new Size(500, double.PositiveInfinity));
        entered.Arrange(new Rect(0, 0, 500, entered.DesiredSize.Height));
        entered.UpdateLayout();
        entered.Update(3000, 3000, true, 0, 0, 5000, 1);
        var enteredMask = Find(entered).OfType<Canvas>().Select(canvas => canvas.OpacityMask).OfType<LinearGradientBrush>().Single();
        check(enteredMask.GradientStops.All(stop => stop.Offset == 0), "entering midway through a word starts the original remaining-duration sweep from the existing mask");
        var enteredGlyph = Find(entered).OfType<TextBlock>().Single(text => text.Text == "w" && text.RenderTransform is TransformGroup);
        check(((ScaleTransform)((TransformGroup)enteredGlyph.RenderTransform).Children[0]).ScaleX == 1, "midword entry dispatches a new character delay instead of resuming an old absolute-time pose");
        entered.Update(3200, 3200, true, 0, 0, 5000, 1);
        check(((ScaleTransform)((TransformGroup)enteredGlyph.RenderTransform).Children[0]).ScaleX > 1 && enteredMask.GradientStops[0].Offset > 0, "new character and mask plans advance after their actual dispatch");
        var shortened = new LyricRow(entered.Line, false, false, 500, 1, new FontFamily("Segoe UI"), new ViewOptions());
        shortened.Measure(new Size(500, double.PositiveInfinity));
        shortened.Arrange(new Rect(0, 0, 500, shortened.DesiredSize.Height));
        shortened.UpdateLayout();
        shortened.Update(3500, 3500, true, 0, 0, 5000, 1);
        shortened.Update(4000, 4000, true, 0, 0, 5000, 1);
        var shortGlyph = Find(shortened).OfType<TextBlock>().Single(text => text.Text == "w" && text.RenderTransform is TransformGroup);
        check(((ScaleTransform)((TransformGroup)shortGlyph.RenderTransform).Children[0]).ScaleX == 1 && ((TranslateTransform)((TransformGroup)shortGlyph.RenderTransform).Children[1]).Y == 2.75, "a shortened last-word dispatch rechecks original iOS emphasis eligibility");
        var skippedLine = new LyricLine { Text = "ab world", StartMs = 1000, EndMs = 5200, Words = [new() { Text = "ab ", StartMs = 1000, DurationMs = 2000, Syllables = [new() { Text = "a", StartMs = 1000, DurationMs = 1000 }, new() { Text = "b ", StartMs = 2000, DurationMs = 1000 }] }, new() { Text = "world", StartMs = 3200, DurationMs = 2000 }] };
        var skipped = new LyricRow(skippedLine, false, false, 500, 1, new FontFamily("Segoe UI"), new ViewOptions());
        skipped.Measure(new Size(500, double.PositiveInfinity));
        skipped.Arrange(new Rect(0, 0, 500, skipped.DesiredSize.Height));
        skipped.UpdateLayout();
        skipped.Update(3500, 3500, true, 0, 0, 6000, 1);
        skipped.Update(4000, 4000, true, 0, 0, 6000, 1);
        var skippedGlyph = Find(skipped).OfType<TextBlock>().Single(text => text.Text == "a" && text.RenderTransform is TransformGroup);
        check(((ScaleTransform)((TransformGroup)skippedGlyph.RenderTransform).Children[0]).ScaleX == 1, "a passed sub-timed word does not start a stale emphasis after the original shared worker selects a later word");
        var plainOptions = new ViewOptions { ReduceMotion = true };
        plainOptions.AppleMusicLyrics.Karaoke = false;
        var plainRow = new LyricRow(tailLine, false, false, 500, 1, new FontFamily("Segoe UI"), plainOptions);
        plainRow.Measure(new Size(500, double.PositiveInfinity));
        plainRow.Arrange(new Rect(0, 0, 500, plainRow.DesiredSize.Height));
        plainRow.UpdateLayout();
        plainRow.Update(1500, 1500, true, 0, 0, 5000);
        check(!Find(plainRow).OfType<Canvas>().Any(c => c.OpacityMask is not null), "turning off karaoke builds plain text instead of hidden word masks");
        check(Find(plainRow).OfType<TextBlock>().Any(t => t.Text == tailLine.Text && t.Visibility == Visibility.Visible && t.FontSize == 42), "plain mode displays the full line at the reference font size");
        check(Math.Abs(plainRow.Frame.ContentScale - 1.05) < 1e-9, "plain mode uses the plain highlight rule");
        using (var fixture = JsonDocument.Parse(typeof(WordEdgeChecks).Assembly.GetManifestResourceStream("early-finish-original.json")!))
            foreach (double width in new[] { 160d, 500d })
                foreach (var item in fixture.RootElement.GetProperty("cases").EnumerateArray())
                {
                    bool floating = item.GetProperty("floating").GetBoolean();
                    var options = new ViewOptions { ReduceMotion = !floating, Float = floating, WordShine = false };
                    options.AppleMusicLyrics.LineSwitchingAppleIosStyle164 = item.GetProperty("ios164").GetBoolean();
                    var line = new LyricLine { Text = "hello world", StartMs = 1000, EndMs = 5000, Words = [new() { Text = "hello ", StartMs = 1000, DurationMs = 200 }, new() { Text = "world", StartMs = item.GetProperty("lastWordStart").GetDouble(), DurationMs = 1000 }] };
                    var row = new LyricRow(line, false, false, width, 1, new FontFamily("Segoe UI"), options) { NextLineStartMs = item.GetProperty("nextStart").GetDouble() };
                    row.Measure(new Size(width, double.PositiveInfinity));
                    row.Arrange(new Rect(0, 0, width, row.DesiredSize.Height));
                    row.UpdateLayout();
                    double initial = item.GetProperty("initialPosition").GetDouble(), observed = item.GetProperty("observedPosition").GetDouble();
                    row.Update(initial, initial, true, 0, 0, 5000, 1);
                    row.Update(observed, observed, true, 0, 0, 5000, 1);
                    bool forced = item.GetProperty("forceFinish").GetBoolean();
                    double lastSample = observed;
                    check(row.FinishingWords == forced, "rendered row follows original early-finish branch, width=" + width + " " + item);
                    if (forced)
                    {
                        double finish = observed + item.GetProperty("durationMs").GetDouble();
                        row.Update(finish, finish, true, 0, 0, 5000, 1);
                        lastSample = finish;
                        foreach (var canvas in Find(row).OfType<Canvas>().Where(canvas => canvas.OpacityMask is LinearGradientBrush))
                        {
                            var mask = (LinearGradientBrush)canvas.OpacityMask;
                            var expected = Motion.Mask(1, canvas.Width - 40, 1, options.AppleMusicLyrics.GetProgressWidth(false));
                            check(Math.Abs(mask.GradientStops[0].Offset - expected.First) < 1e-9 && Math.Abs(mask.GradientStops[1].Offset - expected.Second) < 1e-9, "forced sweep fills the entire padded wrapped strip");
                        }
                    }
                    if (forced && floating && !item.GetProperty("restartInsideGate").GetBoolean())
                    {
                        double midpoint = observed + 668.8;
                        row.Update(midpoint, midpoint, true, 0, 0, 5000, 1);
                        lastSample = midpoint;
                        foreach (var textView in Find(row).OfType<TextBlock>().Where(text => text.Text == "world"))
                            check(textView.RenderTransform is TranslateTransform move && Math.Abs(move.Y - FloatingMotion.EarlyReturn(668.8, 2.75, 41)) < 1e-6, "lowered future words use the original forced-finish float curve across wrapping");
                    }
                    double reflowWidth = width == 160 ? 500 : 160;
                    var reflow = new LyricRow(line, false, false, reflowWidth, 1, new FontFamily("Segoe UI"), options) { NextLineStartMs = 2000 };
                    reflow.Measure(new Size(reflowWidth, double.PositiveInfinity));
                    reflow.Arrange(new Rect(0, 0, reflowWidth, reflow.DesiredSize.Height));
                    reflow.UpdateLayout();
                    reflow.RestoreWordRun(row);
                    reflow.Update(lastSample, lastSample, true, 0, 0, 5000, 1);
                    check(reflow.FinishingWords == forced && reflow.RunningWord == row.RunningWord, "reflow keeps the original word selection instead of restarting inside the early gate");
                    row.Update(1000, 1000, true, 0, 0, 5000, 2);
                    check(!row.FinishingWords && Find(row).OfType<Canvas>().Where(canvas => canvas.OpacityMask is LinearGradientBrush).All(canvas => ((LinearGradientBrush)canvas.OpacityMask).GradientStops.All(stop => stop.Offset == 0)), "seek resets each wrapped strip and the original word worker selection");
                }
        File.WriteAllText(Path.Combine(folder, "word-edges.json"), JsonSerializer.Serialize(new
        {
            passed = true,
            configurations,
            stripCount,
            greatestLoss,
            allowedAlphaLoss = 1,
            windowShown = false,
            scope = "Rendered glyph endpoint and original early-finish branch coverage; not full-page native visual acceptance"
        }, LyricsFile.JsonOptions));
    }
    private static void CheckGlowLifetime(Action<bool, string> check)
    {
        var line = new LyricLine { Text = "ab", StartMs = 1000, EndMs = 3000, Words = [new() { Text = "ab", StartMs = 1000, DurationMs = 2000 }] };
        var window = new Window { Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Window'><ContentPresenter Content='{TemplateBinding Content}'/></ControlTemplate>") };
        window.ApplyTemplate();
        var presenter = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        foreach (var (preset, radius) in new[] { (MotionPreset.Ios, 15.265764615841407), (MotionPreset.Android, 10.568606272505589) })
        {
            var row = new LyricRow(line, false, false, 500, 1, new FontFamily("Segoe UI"), new ViewOptions { Preset = preset }) { SwitchDelayMs = 3000 };
            var host = new Border { Child = row, RenderTransform = new ScaleTransform(.8, .8) };
            window.Content = host;
            presenter.Measure(new Size(500, 200));
            presenter.Arrange(new Rect(0, 0, 500, 200));
            presenter.UpdateLayout();
            var glyph = Find(row).OfType<TextBlock>().Single(text => text.Text == "a" && text.RenderTransform is TransformGroup);
            var word = (Canvas)VisualTreeHelper.GetParent(glyph);
            FrameworkElement target = preset == MotionPreset.Android ? glyph : word;
            check(target.Effect is null, "unstarted emphasis has no glow effect: " + preset);
            row.Update(1000, 1000, true, 0, 1000, 3000, 1, 10000);
            row.Update(1000, 1000, true, 0, 1000, 3000, 1, 10900);
            check(target.Effect is DropShadowEffect glow && glow.Opacity > 0 && Math.Abs(glow.BlurRadius - radius) < 1e-8, "emphasis uses the original relative-window radius at 0.8 scale: " + preset);
            row.Update(3001, 3001, false, 1, 1000, 3000, 1, 10900);
            row.Update(3001, 3001, false, 1, 1000, 3000, 1, 11700);
            check(target.Effect is null, "stopped emphasis releases its effect after the fade: " + preset);
        }
    }
    private static void CheckHanStrokeWeight(Action<bool, string> check)
    {
        foreach (var wrapping in new[] { TextWrapping.NoWrap, TextWrapping.Wrap })
        {
            var baseline = Sample("薄雾越过森林", FontWeights.Bold, wrapping, refined: false);
            var refined = Sample("薄雾越过森林", FontWeights.Bold, wrapping, refined: true);
            check(baseline.Size == refined.Size && refined.Ink > baseline.Ink * .87 && refined.Ink < baseline.Ink * .98,
                $"Han lyric strokes lighten while native layout stays identical, {wrapping}, ink {baseline.Ink} to {refined.Ink}");
        }
        check(Sample("Light through the trees", FontWeights.Bold, TextWrapping.NoWrap, refined: true) ==
              Sample("Light through the trees", FontWeights.Bold, TextWrapping.NoWrap, refined: false) &&
              Sample("薄雾越过森林", FontWeights.Heavy, TextWrapping.NoWrap, refined: true) ==
              Sample("薄雾越过森林", FontWeights.Heavy, TextWrapping.NoWrap, refined: false),
            "Latin lyrics and the explicit extra-bold weight retain their native ink");

        static (Size Size, long Ink) Sample(string text, FontWeight weight, TextWrapping wrapping, bool refined)
        {
            TextBlock block = refined ? new LyricTextBlock() : new TextBlock();
            block.Text = text;
            block.FontFamily = new FontFamily("Microsoft YaHei UI");
            block.FontSize = 39;
            block.FontWeight = weight;
            block.Foreground = Brushes.White;
            block.TextWrapping = wrapping;
            double width = wrapping == TextWrapping.Wrap ? 180 : 500;
            block.MaxWidth = width;
            var surface = new Canvas();
            surface.Children.Add(block);
            surface.Measure(new Size(width, 112));
            surface.Arrange(new Rect(0, 0, width, 112));
            if (block is LyricTextBlock lyric)
                lyric.ApplyHanContour();
            surface.UpdateLayout();
            var image = new RenderTargetBitmap((int)(width * 1.25), 140, 120, 120, PixelFormats.Pbgra32);
            image.Render(surface);
            byte[] pixels = Pixels(image);
            long ink = 0;
            for (int i = 3; i < pixels.Length; i += 4)
                ink += pixels[i];
            return (block.DesiredSize, ink);
        }
    }

    internal static IEnumerable<DependencyObject> Find(DependencyObject node)
    {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        foreach (var child in Find(VisualTreeHelper.GetChild(node, i)))
            yield return child;
    }
    private static RenderTargetBitmap Capture(Canvas canvas)
    {
        var image = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(canvas.Width)), Math.Max(1, (int)Math.Ceiling(canvas.Height)), 96, 96, PixelFormats.Pbgra32);
        image.Render(canvas);
        return image;
    }
    private static byte[] Pixels(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }
}
