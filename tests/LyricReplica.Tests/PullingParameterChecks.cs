using System.Text.Json;
using LyricReplica.Core;

static class PullingParameterChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/pulling-parameters-original.json")));
        check(fixture.RootElement.GetProperty("originalParameterMethodsCalled").GetBoolean() && !fixture.RootElement.GetProperty("originalPullingEffectCalled").GetBoolean(), "pulling fixture distinguishes original scalar calls from recovered delay arithmetic");
        foreach (var item in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            double actual = Motion.PullingDelayMs(item.GetProperty("distance").GetInt32(), item.GetProperty("speed").GetDouble(), item.GetProperty("delay").GetDouble(), item.GetProperty("interline").GetInt32() == 0, item.GetProperty("back").GetBoolean());
            check(actual == item.GetProperty("delayMs").GetDouble(), "pulling delay consumes the selected strength and original interline/back factors: " + item + " actual=" + actual);
        }
        check(Motion.PullingDelayMs(1, delay: double.NaN) == 50 && Motion.PullingDelayMs(1, delay: -1) == 50, "invalid advanced strength values fall back before creating a WPF begin time");
        check(Motion.PullingDelayMs(int.MaxValue, 3, double.MaxValue) == int.MaxValue, "oversized custom strength cannot overflow its native integer time range");
    }
}
