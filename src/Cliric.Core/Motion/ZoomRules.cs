namespace Cliric.Core;

public static class ZoomRules
{
    public static double DefaultInverse(bool portrait, bool maximized, double height) => maximized ? (portrait ? 1000 : 850) / Math.Max(1, height) : portrait ? 1.45 : 1.1;

    public static double Remembered(ZoomLevels levels, bool portrait, bool maximized) =>
        (portrait, maximized) switch
        {
            (false, false) => levels.Horizontal,
            (true, false) => levels.Vertical,
            (false, true) => levels.HorizontalFullscreen,
            _ => levels.VerticalFullscreen
        };

    public static double Restore(AppleMusicSettings settings, bool portrait, bool maximized, double height)
    {
        double saved = Remembered(settings.ZoomingLevels, portrait, maximized);
        return settings.ZoomingRestore && double.IsFinite(saved) && saved > 0 ? saved : DefaultInverse(portrait, maximized, height);
    }

    public static void Remember(AppleMusicSettings settings, bool portrait, bool maximized, double inverse)
    {
        if (!settings.ZoomingRestore || !double.IsFinite(inverse) || inverse <= 0)
            return;
        if (portrait)
        {
            if (maximized)
                settings.ZoomingLevels.VerticalFullscreen = inverse;
            else
                settings.ZoomingLevels.Vertical = inverse;
        }
        else if (maximized)
            settings.ZoomingLevels.HorizontalFullscreen = inverse;
        else
            settings.ZoomingLevels.Horizontal = inverse;
    }

    public static double AdjustInverse(double inverse, bool zoomIn)
    {
        if (!double.IsFinite(inverse) || inverse < .33 || inverse > 2)
            return 1;
        double next = zoomIn ? inverse * .9 : inverse / .9;
        if (next < .33 || next > 2)
            return inverse;
        return next > .93 && next < 1.07 ? 1 : next;
    }
}
