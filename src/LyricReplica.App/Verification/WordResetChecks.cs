using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LyricReplica.Core;

namespace LyricReplica.App;

internal static class WordResetChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(typeof(WordResetChecks).Assembly.GetManifestResourceStream("word-reset-original.json")!);
        var line = new LyricLine { Text = "hi world", StartMs = 1000, EndMs = 5000, Words = [new() { Text = "hi ", StartMs = 1000, DurationMs = 1000 }, new() { Text = "world", StartMs = 2200, DurationMs = 2000 }] };
        foreach (var interline in Enum.GetValues<InterLineAnimationPresetTypes>())
            foreach (bool floating in new[] { false, true })
            {
                var original = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("interline").GetInt32() == (int)interline && item.GetProperty("float").GetBoolean() == floating && item.GetProperty("speed").GetDouble() == 1);
                var options = new ViewOptions { Float = floating, WordShine = false };
                options.AppleMusicLyrics.InterLineAnimationPreset = interline;
                options.AppleMusicLyrics.KaraokeStyleFloatHeight = 8;
                LyricRow Create(double width)
                {
                    var result = new LyricRow(line, false, false, width, 1, new FontFamily("Segoe UI"), options);
                    result.Measure(new Size(width, double.PositiveInfinity));
                    result.Arrange(new Rect(0, 0, width, result.DesiredSize.Height));
                    result.UpdateLayout();
                    return result;
                }
                var row = Create(500);
                double clock = 10000;
                row.AnimationClockNow = () => clock;
                row.Update(3000, 3000, true, 0, 1000, 5000, 1, clock);
                clock = 10600;
                row.Update(3000, 3000, true, 0, 1000, 5000, 1, clock);
                var reset = row.ResetCurrentWordAsync();
                row.Update(1000, 1000, true, 0, 1000, 5000, 2, clock);
                check(row.RunningWord == 1, "current-line reset blocks dispatch immediately after the click seek");
                var reflow = Create(520);
                reflow.RestoreWordRun(row);
                reflow.Update(1000, 1000, true, 0, 1000, 5000, 2, clock);
                clock = 10700;
                await reset;
                check(row.RunningWord == -1, "original reset selects -1 after its first wait and before the next render dispatch");
                var strip = ((IEnumerable)typeof(LyricRow).GetField("_strips", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(row)!).Cast<object>().Single();
                var plan = (WordSweepPlan)strip.GetType().GetProperty("Sweep")!.GetValue(strip)!;
                check(plan.To == original.GetProperty("mask").GetProperty("to").GetDouble() && plan.DurationMs == original.GetProperty("mask").GetProperty("durationMs").GetDouble(), "click reset mask plan matches the original target and duration");
                var oldText = WordEdgeChecks.Find(row).OfType<TextBlock>().Single(text => text.Text == "world" && text.RenderTransform is TranslateTransform);
                double from = ((TranslateTransform)oldText.RenderTransform).Y;
                clock = 10800;
                reflow.Update(1000, 1000, true, 0, 1000, 5000, 2, clock);
                check(reflow.RunningWord == 0 && WordEdgeChecks.Find(reflow).OfType<Canvas>().Where(canvas => canvas.OpacityMask is LinearGradientBrush).Any(canvas => ((LinearGradientBrush)canvas.OpacityMask).GradientStops[0].Offset > 0), "reset survives reflow and resumes from the in-flight reset mask snapshot");
                var future = WordEdgeChecks.Find(reflow).OfType<TextBlock>().Single(text => text.Text == "world" && text.RenderTransform is TranslateTransform);
                double expected = floating ? from + (original.GetProperty("ordinaryFloat").GetProperty("to").GetDouble() - from) * 100 / original.GetProperty("ordinaryFloat").GetProperty("durationMs").GetDouble() : 0;
                check(Math.Abs(((TranslateTransform)future.RenderTransform).Y - expected) < 1e-6, "future ordinary-word reset lift matches the original linear target and duration across reflow");
            }
        foreach (var preset in new[] { MotionPreset.Ios, MotionPreset.Lyricify })
        {
            var options = new ViewOptions { Preset = preset };
            options.AppleMusicLyrics.KaraokeStyleFloatHeight = 8;
            var row = new LyricRow(line, false, false, 500, 1, new FontFamily("Segoe UI"), options);
            row.Measure(new Size(500, double.PositiveInfinity));
            row.Arrange(new Rect(0, 0, 500, row.DesiredSize.Height));
            row.UpdateLayout();
            double clock = 10000;
            row.AnimationClockNow = () => clock;
            row.Update(3000, 3000, true, 0, 1000, 5000, 1, clock);
            clock = 10600;
            row.Update(3000, 3000, true, 0, 1000, 5000, 1, clock);
            var glyphs = WordEdgeChecks.Find(row).OfType<TextBlock>().Where(text => text.RenderTransform is TransformGroup).ToArray();
            var first = (TransformGroup)glyphs.Single(text => text.Text == "w").RenderTransform;
            var firstMove = (TranslateTransform)first.Children[1];
            var firstScale = (ScaleTransform)first.Children[0];
            var reset = row.ResetCurrentWordAsync();
            row.Update(1000, 1000, true, 0, 1000, 5000, 2, clock);
            clock = 10700;
            await reset;
            double fromY = firstMove.Y, fromScale = firstScale.ScaleX;
            clock = 10800;
            row.Update(1000, 1000, true, 0, 1000, 5000, 2, clock);
            var original = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("interline").GetInt32() == 0 && item.GetProperty("float").GetBoolean() && item.GetProperty("speed").GetDouble() == 1).GetProperty("ordinaryFloat");
            double expected = fromY + (original.GetProperty("to").GetDouble() - fromY) * 100 / original.GetProperty("durationMs").GetDouble();
            check(Math.Abs(firstMove.Y - expected) < 1e-6, "click reset overrides emphasized glyph Y with the original positive linear return: " + preset);
            if (preset == MotionPreset.Ios)
                check(firstScale.ScaleX <= fromScale, "click reset stops the modern glyph spring instead of continuing its growth");
            else
            {
                clock = 12000;
                row.Update(1000, 1000, true, 0, 1000, 5000, 2, clock);
                var last = (TransformGroup)glyphs.Single(text => text.Text == "d").RenderTransform;
                check(((ScaleTransform)last.Children[0]).ScaleX == 1, "click reset cancels a future legacy character growth while its independent return remains allowed");
            }
        }
        var inactive = new LyricRow(line, false, false, 500, 1, new FontFamily("Segoe UI"), new ViewOptions());
        var ignored = inactive.ResetCurrentWordAsync();
        check(ignored.IsCompletedSuccessfully && inactive.RunningWord == -1, "a non-running line does not start the click-reset wait");
    }
}
