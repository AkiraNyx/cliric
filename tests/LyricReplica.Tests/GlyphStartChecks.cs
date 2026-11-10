using System.Text.Json;
using LyricReplica.Core;

static class GlyphStartChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/glyph-start-original.json")));
        check(fixture.RootElement.GetProperty("originalPreparationCalled").GetBoolean(), "preparation fixture invokes the original TextSing method");
        foreach (var item in fixture.RootElement.GetProperty("preparation").EnumerateArray())
        {
            double height = item.GetProperty("heightSetting").GetDouble() > 0 ? item.GetProperty("heightSetting").GetDouble() / 2 : 2.75;
            var plan = item.GetProperty("plan");
            foreach (var point in plan.GetProperty("samples").EnumerateArray())
            {
                double elapsed = plan.GetProperty("durationMs").GetDouble() * point.GetProperty("fraction").GetDouble();
                double actual = FloatingMotion.Y(elapsed, 100000, 1000, 0, item.GetProperty("fontSize").GetDouble(), floatHeight: height, speed: item.GetProperty("speed").GetDouble(), ios: item.GetProperty("interline").GetInt32() == 0);
                check(Math.Abs(actual - point.GetProperty("value").GetDouble()) < 1e-6, "original float preparation elapsed=" + elapsed + ": " + actual + " vs " + point.GetProperty("value").GetDouble());
            }
        }
        check(fixture.RootElement.GetProperty("originalIosCharacterOrchestratorCalled").GetBoolean(), "glyph fixtures invoke the original iOS character orchestrator");
        foreach (var item in fixture.RootElement.GetProperty("glyphs").EnumerateArray())
        {
            double height = item.GetProperty("heightSetting").GetDouble() > 0 ? item.GetProperty("heightSetting").GetDouble() / 2 : 2.75, font = item.GetProperty("fontSize").GetDouble();
            var word = new LyricWord { Text = "a", StartMs = 1000, DurationMs = item.GetProperty("wordDurationMs").GetDouble() };
            foreach (string part in new[] { "scale", "move" })
            {
                var plan = item.GetProperty(part);
                foreach (var point in plan.GetProperty("samples").EnumerateArray())
                {
                    double time = word.StartMs + item.GetProperty("growDelayMs").GetDouble() + plan.GetProperty("durationMs").GetDouble() * point.GetProperty("fraction").GetDouble();
                    var pose = Motion.Glyph(time, word, 0, 1, item.GetProperty("ios264").GetBoolean() ? MotionPreset.Ios264 : MotionPreset.Ios, font, 50 * font / 41, true, height, item.GetProperty("fromY").GetDouble(), item.GetProperty("initialScale").GetDouble());
                    check(Math.Abs((part == "scale" ? pose.Scale : pose.Y) - point.GetProperty("value").GetDouble()) < 1e-6, "original current-transform character start " + part + " " + item.GetProperty("ios264") + " " + item.GetProperty("heightSetting") + " time=" + time + " actual=" + (part == "scale" ? pose.Scale : pose.Y) + " expected=" + point.GetProperty("value").GetDouble());
                }
            }
        }
    }
}
