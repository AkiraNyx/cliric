namespace Cliric.Core;

public sealed record LineAnimationPlan(LineFrame From, LineFrame To, bool Playing, bool Timed, bool Ios, bool UseBackEase, double ScrollGap,
    double PlayedDurationMs, double UnplayedDurationMs, double SecondaryDurationMs, double ScaleDurationMs)
{
    public double ScaleDelayMs
    {
        get; init;
    }
    public LineFrame At(double elapsedMs)
    {
        double scaleProgress = ScaleDurationMs <= 0 ? 1 : Math.Clamp((elapsedMs - ScaleDelayMs) / ScaleDurationMs, 0, 1);
        double growth = Ios ? Motion.Scroll(scaleProgress, UseBackEase ? ScrollGap : null) : UseBackEase ? Motion.LegacyBackScroll(scaleProgress) : Motion.LegacyScroll(scaleProgress);
        return To with
        {
            ContentScale = From.ContentScale + (To.ContentScale - From.ContentScale) * growth,
            PlayedAlpha = Lerp(From.PlayedAlpha, To.PlayedAlpha, Fraction(PlayedDurationMs, Timed)),
            UnplayedAlpha = Lerp(From.UnplayedAlpha, To.UnplayedAlpha, Fraction(UnplayedDurationMs, Timed)),
            SecondaryAlpha = Lerp(From.SecondaryAlpha, To.SecondaryAlpha, Fraction(SecondaryDurationMs, Timed && Playing))
        };
        double Fraction(double duration, bool power)
        {
            double p = duration <= 0 ? 1 : Math.Clamp(elapsedMs / duration, 0, 1);
            return power ? 1 - Math.Pow(1 - p, 4.3) : p;
        }
        static double Lerp(double from, double to, double progress) => from + (to - from) * progress;
    }
}
