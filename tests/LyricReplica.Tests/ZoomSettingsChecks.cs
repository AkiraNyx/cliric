using System.Text.Json;
using LyricReplica.Core;

static class ZoomSettingsChecks
{
    public static void Run(Action<bool, string> check)
    {
        var settings = new AppleMusicSettings();
        var contexts = new (bool Portrait, bool Maximized, double Height, double Default, double Saved)[] { (false, false, 820, 1.1, .8), (true, false, 1000, 1.45, 1.3), (false, true, 900, 850d / 900, .9), (true, true, 1200, 1000d / 1200, 1.1) };
        foreach (var context in contexts)
        {
            check(ZoomRules.Restore(settings, context.Portrait, context.Maximized, context.Height) == context.Default, "unset zoom uses the recovered scene default " + context);
            ZoomRules.Remember(settings, context.Portrait, context.Maximized, context.Saved);
        }
        foreach (var context in contexts)
        {
            check(ZoomRules.Restore(settings, context.Portrait, context.Maximized, context.Height + 200) == context.Saved, "four zoom memories remain independent of later window size " + context);
            settings.ZoomingRestore = false;
            ZoomRules.Remember(settings, context.Portrait, context.Maximized, .5);
            check(ZoomRules.Restore(settings, context.Portrait, context.Maximized, context.Height) == context.Default && ZoomRules.Remembered(settings.ZoomingLevels, context.Portrait, context.Maximized) == context.Saved, "disabled zoom memory neither restores nor overwrites a scene " + context);
            settings.ZoomingRestore = true;
        }
        var restored = JsonSerializer.Deserialize<AppleMusicSettings>(JsonSerializer.Serialize(settings))!;
        check(restored.ZoomingLevels.Horizontal == .8 && restored.ZoomingLevels.Vertical == 1.3 && restored.ZoomingLevels.HorizontalFullscreen == .9 && restored.ZoomingLevels.VerticalFullscreen == 1.1, "inverse zoom values roundtrip with the original four keys");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -1 })
        {
            settings.ZoomingLevels.Horizontal = invalid;
            check(ZoomRules.Restore(settings, false, false, 820) == 1.1, "invalid stored inverse zoom cannot produce a nonfinite or negative layout " + invalid);
        }
    }
}
