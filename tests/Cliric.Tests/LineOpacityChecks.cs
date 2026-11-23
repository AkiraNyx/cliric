using System.Text.Json;
using Cliric.Core;

static class LineOpacityChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/line-opacity-original.json")));
        foreach (string file in new[] { "line-opacity-original.json", "line-opacity-classic-original.json" })
        {
            using var colours = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file)));
            check(colours.RootElement.GetProperty("singOpacityDuration").GetInt32() == colours.RootElement.GetProperty("durationAfterSettingToggle").GetInt32(), "original type retains its initial sing opacity duration after toggling settings");
            check(colours.RootElement.GetProperty("originalPrepareForSingCalled").GetBoolean() && colours.RootElement.GetProperty("originalStopCalled").GetBoolean(), "line opacity fixtures call original PrepareForSing and Stop");
            foreach (var item in colours.RootElement.GetProperty("cases").EnumerateArray())
            {
                bool backEase = item.GetProperty("backEase").GetBoolean();
                var settings = new AppleMusicSettings { InterLineAnimationPreset = (InterLineAnimationPresetTypes)item.GetProperty("interline").GetInt32(), LineSwitchingOpacitySpeedAppleIosStyle164 = item.GetProperty("instant").GetBoolean(), LineSwitchingAppleIosStyle164 = colours.RootElement.GetProperty("lineSwitchingIos164").GetBoolean() };
                bool background = item.GetProperty("background").GetBoolean();
                double speed = item.GetProperty("speed").GetDouble();
                foreach (string phase in new[] { "prepared", "stopped" })
                    for (int color = 0; color < 2; color++)
                    {
                        var plan = item.GetProperty(phase)[color];
                        double duration = plan.GetProperty("durationMs").GetDouble();
                        foreach (var sample in plan.GetProperty("samples").EnumerateArray())
                        {
                            double elapsed = Math.Max(1e-7, duration * sample.GetProperty("fraction").GetDouble());
                            var initial = LineMotion.At(0, 1000, 10000, true, background, false, speed, settings: settings);
                            if (phase == "stopped")
                                initial = initial with
                                {
                                    PlayedAlpha = item.GetProperty("prepared")[0].GetProperty("toAlpha").GetDouble() / 255,
                                    UnplayedAlpha = item.GetProperty("prepared")[1].GetProperty("toAlpha").GetDouble() / 255
                                };
                            var frame = LineMotion.Start(initial, phase == "prepared", true, background, false, speed, settings: settings, useBackEase: backEase).At(elapsed);
                            double alpha = (color == 0 ? frame.PlayedAlpha : frame.UnplayedAlpha) * 255;
                            int expected = sample.GetProperty("alpha").GetInt32();
                            check(Math.Abs(Math.Round(alpha) - expected) <= 1, $"original {phase} mask color {color}, preset={settings.InterLineAnimationPreset}, speed={speed}, instant={settings.LineSwitchingOpacitySpeedAppleIosStyle164}, bg={background}, elapsed={elapsed}: {alpha} vs {expected}");
                        }
                    }
            }
            check(colours.RootElement.GetProperty("originalLineStateSetterCalled").GetBoolean(), "secondary opacity fixtures call the original line State setter");
            foreach (var item in colours.RootElement.GetProperty("secondary").EnumerateArray())
            {
                bool timed = item.GetProperty("timed").GetBoolean();
                double speed = item.GetProperty("speed").GetDouble();
                var settings = new AppleMusicSettings { LineSwitchingAppleIosStyle164 = colours.RootElement.GetProperty("lineSwitchingIos164").GetBoolean(), TranslationLinePreset = (TranslationLinePresetTypes)item.GetProperty("preset").GetInt32() };
                foreach (string phase in new[] { "enter", "leave" })
                    foreach (var sample in item.GetProperty(phase).GetProperty("samples").EnumerateArray())
                    {
                        double elapsed = Math.Max(1e-7, item.GetProperty(phase).GetProperty("durationMs").GetDouble() * sample.GetProperty("fraction").GetDouble());
                        var initial = LineMotion.At(0, 1000, 10000, timed, false, false, speed, settings: settings);
                        if (phase == "leave")
                            initial = initial with
                            {
                                SecondaryAlpha = item.GetProperty("enter").GetProperty("to").GetDouble()
                            };
                        var frame = LineMotion.Start(initial, phase == "enter", timed, false, false, speed, settings: settings).At(elapsed);
                        double expected = sample.GetProperty("value").GetDouble();
                        check(Math.Abs(frame.SecondaryAlpha - expected) < 1e-6, $"original secondary {phase}, timed={timed}, speed={speed}, style164={settings.LineSwitchingAppleIosStyle164}, preset={settings.TranslationLinePreset}, elapsed={elapsed}: {frame.SecondaryAlpha} vs {expected}");
                    }
            }
        }
        using (var scales = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/line-scale-original.json"))))
        {
            check(scales.RootElement.GetProperty("originalScaleHelperCalled").GetBoolean(), "line scale fixtures call the original three-argument scale helper");
            foreach (var item in scales.RootElement.GetProperty("cases").EnumerateArray())
            {
                bool timed = item.GetProperty("timed").GetBoolean();
                var settings = new AppleMusicSettings { InterLineAnimationPreset = (InterLineAnimationPresetTypes)item.GetProperty("preset").GetInt32(), PlayingZoomOverride = 1.02 };
                var initial = LineMotion.At(0, 1000, 10000, timed, false, false, settings: settings) with
                {
                    ContentScale = item.GetProperty("from").GetDouble()
                };
                var plan = LineMotion.Start(initial, item.GetProperty("phase").GetString() == "enter", timed, false, false, settings: settings, useBackEase: item.GetProperty("backEase").GetBoolean(), scrollGap: item.GetProperty("gap").GetDouble());
                check(Math.Abs(plan.ScaleDurationMs - item.GetProperty("durationMs").GetDouble()) <= .0001, "line scale duration matches original within one TimeSpan tick: " + item.GetProperty("easing").GetString());
                foreach (var sample in item.GetProperty("samples").EnumerateArray())
                {
                    double elapsed = sample.GetProperty("fraction").GetDouble() == 1 ? plan.ScaleDurationMs : sample.GetProperty("elapsedMs").GetDouble();
                    check(Math.Abs(plan.At(elapsed).ContentScale - sample.GetProperty("value").GetDouble()) < 1e-8, "line scale sample matches the original curve and current-scale snapshot: " + item.GetProperty("easing").GetString());
                }
            }
        }
        var shortBefore = LineMotion.At(1100, 1000, 1100, true, false, false);
        var shortAfter = LineMotion.At(1100 + 1e-7, 1000, 1100, true, false, false);
        check(Math.Abs(shortBefore.PlayedAlpha - shortAfter.PlayedAlpha) < 1e-8 && Math.Abs(shortBefore.UnplayedAlpha - shortAfter.UnplayedAlpha) < 1e-8, "short lines stop from their partially entered colors without jumping to full opacity");
        var ordinarySettings = new AppleMusicSettings { TranslationLinePreset = TranslationLinePresetTypes.iOS };
        shortBefore = LineMotion.At(1100, 1000, 1100, false, false, false, settings: ordinarySettings);
        shortAfter = LineMotion.At(1100 + 1e-7, 1000, 1100, false, false, false, settings: ordinarySettings);
        check(Math.Abs(shortBefore.PlayedAlpha - shortAfter.PlayedAlpha) < 1e-8 && Math.Abs(shortBefore.SecondaryAlpha - shortAfter.SecondaryAlpha) < 1e-8, "short ordinary lines keep both text and translation opacity continuous when stopping");
        var switched = new AppleMusicSettings { LineSwitchingAppleIosStyle164 = false };
        check(LineMotion.At(10175, 1000, 10000, true, false, false, settings: switched, singOpacityDuration: 700).PlayedAlpha > LineMotion.At(10175, 1000, 10000, true, false, false, settings: switched).PlayedAlpha, "a captured original stop duration survives later setting changes");
        var rows = new List<DisplayLine> { new(new() { StartMs = 1000, EndMs = 2000, Words = [new() { StartMs = 1000, DurationMs = 1000 }] }, [], 500, false), new(new() { StartMs = 2600, EndMs = 4000, Words = [new() { StartMs = 2600, DurationMs = 1400 }] }, [], 2200, false) };
        var ios = new AppleMusicSettings();
        check(LineMotion.PageTransition(rows, 0, true, ios) == (true, .2) && LineMotion.PageTransition(rows, 1, true, ios) == (true, .6), "iOS color transition uses the true word gap after the first row");
        rows[1] = rows[1] with
        {
            StartMs = 9000
        };
        check(LineMotion.PageTransition(rows, 1, true, ios) == (true, .2), "breathing gaps restore the source short response");
        check(LineMotion.PageTransition(rows, 1, false, ios) == (false, .2), "ordinary rows bypass karaoke color transition selection");
        check(fixture.RootElement.GetProperty("originalPageTransitionCalled").GetBoolean(), "transition fixtures invoke the original page helper");
        foreach (var item in fixture.RootElement.GetProperty("pageTransitions").EnumerateArray())
        {
            var nativeRows = item.GetProperty("rows").EnumerateArray().Select(row => new DisplayLine(new() { Text = "probe", StartMs = row.GetProperty("rawStart").GetDouble(), EndMs = row.GetProperty("rawEnd").GetDouble(), Words = [new() { Text = "probe", StartMs = row.GetProperty("rawStart").GetDouble(), DurationMs = row.GetProperty("rawEnd").GetDouble() - row.GetProperty("rawStart").GetDouble() }] }, [], row.GetProperty("start").GetDouble(), false, PreparedEndMs: row.GetProperty("end").GetDouble())).ToList();
            var selected = LineMotion.PageTransition(nativeRows, item.GetProperty("selected").GetInt32(), true, new()
            {
                InterLineAnimationPreset = (InterLineAnimationPresetTypes)item.GetProperty("preset").GetInt32()
            });
            bool gapMatches = item.GetProperty("preset").GetInt32() != 0 || Math.Abs(selected.GapSeconds - item.GetProperty("gapSeconds").GetDouble()) < 1e-9;
            check(selected.UseBackEase == item.GetProperty("useBackEase").GetBoolean() && gapMatches, "original page BackEase and applicable true-gap selection " + item.GetProperty("name").GetString());
        }
    }
}
