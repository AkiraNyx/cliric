using System.Text.Json;
using LyricReplica.Core;

static class LineGlyphStopChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/line-glyph-stop-original.json")));
        check(fixture.RootElement.GetProperty("outerTextSingStopCalled").GetBoolean() && fixture.RootElement.GetProperty("cases").GetArrayLength() == 48, "line glyph fixture calls original outer TextSing.Stop across 48 configurations");
        foreach (var item in fixture.RootElement.GetProperty("cases").EnumerateArray())
            foreach (var plan in item.GetProperty("snapshots")[0].GetProperty("moves").EnumerateArray())
            {
                var samples = plan.GetProperty("samples");
                double from = samples[0].GetProperty("value").GetDouble();
                foreach (var sample in samples.EnumerateArray())
                {
                    double elapsed = plan.GetProperty("durationMs").GetDouble() * sample.GetProperty("fraction").GetDouble();
                    double actual = FloatingMotion.LineReturn(elapsed, from, item.GetProperty("speed").GetDouble(), item.GetProperty("interline").GetInt32() == 0);
                    check(Math.Abs(actual - sample.GetProperty("value").GetDouble()) < 1e-6, "original outer line return overrides word Y, preset=" + item.GetProperty("preset") + " immediate=" + item.GetProperty("immediate") + " elapsed=" + elapsed);
                }
            }
        check(FloatingMotion.LineReturn(0, 4, .1, false) == 0, "zero-duration classic baseline return completes immediately");
    }
}
