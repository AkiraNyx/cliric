namespace Cliric.Core;

public static class BackgroundSpring
{
    public static double Duration(bool showing)
    {
        if (showing)
        {
            double damping = 9 / (2 * Math.Sqrt(30));
            return Math.Log(1 / Math.Sqrt(1 - damping * damping) / .001) / 4.5;
        }
        double lower = 0, upper = 1;
        for (int i = 0; i < 64; i++)
        {
            double middle = (lower + upper) / 2;
            if ((1 + Math.PI * 10 * middle) * Math.Exp(-Math.PI * 10 * middle) > .001)
                lower = middle;
            else
                upper = middle;
        }
        return upper;
    }
    public static double Ease(bool showing, double progress)
    {
        if (progress <= 0)
            return 0;
        if (progress >= 1)
            return 1;
        double time = progress * Duration(showing);
        if (!showing)
            return 1 - (1 + Math.PI * 10 * time) * Math.Exp(-Math.PI * 10 * time);
        double frequency = Math.Sqrt(30 - 4.5 * 4.5);
        return 1 - Math.Exp(-4.5 * time) * (Math.Cos(frequency * time) + 4.5 / frequency * Math.Sin(frequency * time));
    }
}

public readonly record struct LineFrame(double RootOpacity, double RootScale, double ContentScale, double PlayedAlpha, double UnplayedAlpha, double SecondaryAlpha);

public static class LineMotion
{
    public static LineAnimationPlan Start(LineFrame initial, bool playing, bool timed, bool background, bool blur, double speed = 1, bool reduceMotion = false,
        AppleMusicSettings? settings = null, bool hasBackgroundVocals = false, bool useBackEase = false, double scrollGap = .2, double? singOpacityDuration = null)
    {
        double singDuration = singOpacityDuration ?? (settings?.LineSwitchingAppleIosStyle164 != false ? 700 : 350);
        bool ios = settings?.InterLineAnimationPreset != InterLineAnimationPresetTypes.Cliric;
        bool sudden = settings?.BuggyApple == true && (settings.BuggyAppleOptions.LyricsLineTransparencySuddenChange || hasBackgroundVocals && settings.BuggyAppleOptions.BackgroundVocalTransparencySuddenChange);
        double scrollDuration = ios ? Math.Round(Motion.ScrollParameters().Duration * 1000) : timed ? 500 : 620;
        double ratio = useBackEase ? (ios ? Motion.ScrollParameters(scrollGap).Duration / Motion.ScrollParameters().Duration : 2.26) : 1;
        double primary = PrimaryAlpha(timed, blur, settings), played, unplayed, secondary;
        double playedDuration, unplayedDuration, secondaryDuration;
        if (playing)
        {
            played = timed && background ? 159d / 255 : 1;
            unplayed = timed ? (settings?.GetPreplayOpacity() ?? 71) / 255d : played;
            secondary = settings?.GetSecondaryAlpha(timed, background) ?? (timed ? (background ? .5 : .7) : 1);
            bool instant = settings?.LineSwitchingOpacitySpeedAppleIosStyle164 == true;
            playedDuration = timed ? (instant ? 0 : 200 * speed) : 150 * speed;
            unplayedDuration = timed ? (instant ? 0 : scrollDuration * .85 * ratio * speed) : playedDuration;
            secondaryDuration = (timed ? singDuration : 150) * speed;
        }
        else
        {
            played = unplayed = primary;
            secondary = timed ? 47d / 255 : 1;
            playedDuration = unplayedDuration = timed ? (sudden ? 10 : Math.Truncate(singDuration * speed)) : 150 * speed;
            secondaryDuration = !timed ? 150 * speed : sudden ? 10 : settings?.LineSwitchingAppleIosStyle164 != false ? 350 : 100;
        }
        if (timed)
            initial = initial with
            {
                PlayedAlpha = Math.Round(initial.PlayedAlpha * 255) / 255,
                UnplayedAlpha = Math.Round(initial.UnplayedAlpha * 255) / 255
            };
        var target = initial with
        {
            ContentScale = background ? initial.ContentScale : playing ? settings?.GetHighlightScale(timed) ?? (timed ? 1.02 : blur ? 1.1 : 1.05) : 1,
            PlayedAlpha = played,
            UnplayedAlpha = unplayed,
            SecondaryAlpha = secondary
        };
        return new(initial, target, playing, timed, ios, useBackEase, scrollGap, Duration(playedDuration), Duration(unplayedDuration), Duration(secondaryDuration), Duration(scrollDuration * (ios ? 1 : .98) * ratio * speed));
        double Duration(double value) => reduceMotion ? 0 : TimeSpan.FromMilliseconds(value).TotalMilliseconds;
    }
    private static double PrimaryAlpha(bool timed, bool blur, AppleMusicSettings? settings)
    {
        if (timed)
            return 47d / 255;
        double opacity = settings?.UnplayingOpacityOverride ?? double.NaN;
        return double.IsNormal(opacity) && opacity > 0 && opacity < 1 ? opacity : blur ? .4 : .3;
    }
    public static (bool UseBackEase, double GapSeconds) PageTransition(IReadOnlyList<DisplayLine> rows, int index, bool karaoke, AppleMusicSettings settings)
    {
        if (!karaoke)
            return (false, .2);
        var current = rows[index];
        if (settings.InterLineAnimationPreset != InterLineAnimationPresetTypes.iOS)
            return (current.Main.Words.Count > 0 && current.Main.Words[0].StartMs - current.StartMs >= 450, .2);
        if (index == 0 || current.StartMs - rows[index - 1].EndMs > 6000)
            return (true, .2);
        var previous = rows[index - 1];
        double first = current.Backgrounds.Append(current.Main).Min(line => line.Words.Count > 0 ? line.Words[0].StartMs : line.StartMs);
        double last = previous.Backgrounds.Append(previous.Main).Max(line => line.Words.Count > 0 ? line.Words[^1].EndMs : line.EffectiveEndMs);
        return (true, (first - last) / 1000);
    }
    public static LineFrame At(double time, double start, double end, bool timed, bool background, bool blur, double speed = 1, bool reduceMotion = false, AppleMusicSettings? settings = null, bool hasBackgroundVocals = false, bool useBackEase = false, double scrollGap = .2, double? singOpacityDuration = null)
    {
        bool active = time > start && (end == 0 || time <= end), past = end > 0 && time > end;
        double elapsed = active ? time - start : past ? time - end : 0;
        double singDuration = singOpacityDuration ?? (settings?.LineSwitchingAppleIosStyle164 != false ? 700 : 350);
        bool sudden = settings?.BuggyApple == true && (settings.BuggyAppleOptions.LyricsLineTransparencySuddenChange || hasBackgroundVocals && settings.BuggyAppleOptions.BackgroundVocalTransparencySuddenChange);
        double p = reduceMotion ? 1 : Math.Clamp(elapsed / ((timed ? singDuration : 150) * speed), 0, 1), fade = timed ? 1 - Math.Pow(1 - p, 4.3) : p;
        double target = settings?.GetHighlightScale(timed) ?? (timed ? 1.02 : blur ? 1.1 : 1.05);
        double growth = reduceMotion ? 1 : Motion.Scroll(elapsed / (Motion.ScrollParameters().Duration * 1000 * speed));
        double contentScale = active ? 1 + (target - 1) * growth : past ? target + (1 - target) * growth : 1;
        double primary = PrimaryAlpha(timed, blur, settings);
        double playedTarget = timed && background ? 159d / 255 : 1, unplayedTarget = timed ? (settings?.GetPreplayOpacity() ?? 71) / 255d : playedTarget;
        double secondaryTarget = settings?.GetSecondaryAlpha(timed, background) ?? (timed ? (background ? .5 : .7) : 1);
        double played = primary, unplayed = primary, secondary = timed ? 55d / 255 : 1;
        bool ios = settings?.InterLineAnimationPreset != InterLineAnimationPresetTypes.Cliric;
        double scrollDuration = ios ? Math.Round(Motion.ScrollParameters().Duration * 1000) : 500;
        double backRatio = ios ? Motion.ScrollParameters(scrollGap).Duration / Motion.ScrollParameters().Duration : 2.26;
        double playedEnter = settings?.LineSwitchingOpacitySpeedAppleIosStyle164 == true ? 0 : 200 * speed;
        double unplayedEnter = settings?.LineSwitchingOpacitySpeedAppleIosStyle164 == true ? 0 : scrollDuration * .85 * (useBackEase ? backRatio : 1) * speed;
        if (active)
        {
            played = primary + (playedTarget - primary) * (timed ? ColorFade(elapsed, playedEnter) : fade);
            unplayed = timed ? primary + (unplayedTarget - primary) * ColorFade(elapsed, unplayedEnter) : played;
            secondary = timed ? 55d / 255 + (secondaryTarget - 55d / 255) * fade : 1 + (secondaryTarget - 1) * fade;
        }
        else if (past)
        {
            double stopDuration = sudden ? 10 : Math.Truncate(singDuration * speed);
            double alphaFade = timed ? ColorFade(elapsed, stopDuration) : fade;
            double ordinaryEntry = reduceMotion ? 1 : Math.Clamp((end - start) / (150 * speed), 0, 1);
            double fromPlayed = primary + (playedTarget - primary) * (timed ? ColorFade(end - start, playedEnter) : ordinaryEntry);
            double fromUnplayed = primary + (unplayedTarget - primary) * ColorFade(end - start, unplayedEnter);
            played = fromPlayed + (primary - fromPlayed) * alphaFade;
            unplayed = timed ? fromUnplayed + (primary - fromUnplayed) * alphaFade : played;
            double fromSecondary = timed ? 55d / 255 + (secondaryTarget - 55d / 255) * ColorFade(end - start, singDuration * speed) : 1 + (secondaryTarget - 1) * ordinaryEntry;
            double secondaryDuration = !timed ? 150 * speed : sudden ? 10 : settings?.LineSwitchingAppleIosStyle164 != false ? 350 : 100;
            secondary = fromSecondary + ((timed ? 47d / 255 : 1) - fromSecondary) * (reduceMotion ? 1 : Math.Clamp(elapsed / secondaryDuration, 0, 1));
        }
        if (!background)
            return new(1, 1, contentScale, played, unplayed, secondary);
        double bg = reduceMotion ? 1 : BackgroundSpring.Ease(active, elapsed / (BackgroundSpring.Duration(active) * 1000 * speed));
        double opacity = active ? .66 * bg : past ? .66 * (1 - bg) : 0, scale = active ? .9 + .1 * bg : past ? 1 - .1 * bg : .9;
        return new(Math.Clamp(opacity, 0, 1), scale, contentScale, played, unplayed, secondary);
        double ColorFade(double elapsedMs, double durationMs) => reduceMotion || durationMs <= 0 ? 1 : 1 - Math.Pow(1 - Math.Clamp(elapsedMs / durationMs, 0, 1), 4.3);
    }
}
