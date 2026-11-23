using System.Text.Json;
using Cliric.Core;

static class BackgroundStateChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/background-state-original.json")));
        check(fixture.RootElement.GetProperty("originalBackgroundStateSetterCalled").GetBoolean(), "background fixtures call the original State setter");
        foreach (var item in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            bool playing = item.GetProperty("phase").GetString() == "enter", above = item.GetProperty("above").GetBoolean();
            var settings = new AppleMusicSettings { InterLineAnimationPreset = (InterLineAnimationPresetTypes)item.GetProperty("preset").GetInt32(), BackgroundVocalsEnterAnimation = item.GetProperty("mode").GetInt32() };
            var initial = new LineFrame(item.GetProperty("opacity").GetProperty("defaultFrom").GetDouble(), item.GetProperty("scale").GetProperty("defaultFrom").GetDouble(), 1, 47d / 255, 47d / 255, 55d / 255);
            var plan = BackgroundMotion.Start(initial, playing, item.GetProperty("height").GetDouble(), above, 1, settings);
            foreach (string component in new[] { "opacity", "scale", "y", "parentY" })
            {
                var original = item.GetProperty(component);
                if (original.ValueKind == JsonValueKind.Null)
                {
                    check(component == "parentY" && !above && plan.At(0).ParentOffsetY == 0, "below-main background leaves its parent text stationary");
                    continue;
                }
                double duration = component == "opacity" ? plan.OpacityDurationMs : component == "scale" ? plan.ScaleDurationMs : plan.MovementDurationMs;
                check(Math.Abs(duration - original.GetProperty("durationMs").GetDouble()) <= .0001, "background plan duration matches original " + component);
                foreach (var sample in original.GetProperty("samples").EnumerateArray())
                {
                    double elapsed = sample.GetProperty("fraction").GetDouble() == 1 ? duration : sample.GetProperty("elapsedMs").GetDouble();
                    var state = plan.At(elapsed);
                    double value = component switch
                    {
                        "opacity" => state.Opacity,
                        "scale" => state.Scale,
                        "y" => state.Y,
                        _ => state.ParentOffsetY
                    };
                    check(Math.Abs(value - sample.GetProperty("value").GetDouble()) < 1e-8, "background state matches original " + component + " preset=" + settings.InterLineAnimationPreset + " mode=" + settings.BackgroundVocalsEnterAnimation + " phase=" + item.GetProperty("phase").GetString());
                }
            }
            var back = BackgroundMotion.Start(initial, playing, item.GetProperty("height").GetDouble(), above, 1, settings, useBackEase: true);
            check(back.MovementDurationMs == item.GetProperty("movementBackDurationMs").GetDouble(), "background movement back-ease duration uses the original line helper multiplier");
        }
    }
}
