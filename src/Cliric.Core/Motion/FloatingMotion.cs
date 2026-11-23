namespace Cliric.Core;

public static class FloatingMotion
{
    public static double LineReturn(double elapsedMs, double from, double speed = 1, bool ios = true)
    {
        double duration = (ios ? Math.Round(Motion.ScrollParameters(.2).Duration * 1000) : 500) * speed - 50;
        return duration <= 0 ? 0 : from * (1 - Math.Clamp(elapsedMs / duration, 0, 1));
    }
    public static double EarlyReturn(double elapsedMs, double from, double fontSize, double floatHeight = 2.75) => from + (-floatHeight * fontSize / 41 - from) * Ease(elapsedMs / 1337.6);
    public static double Prepare(double time, double lineStart, double fontSize, double floatHeight = 2.75, double speed = 1, bool ios = true, double initialY = 0)
    {
        double duration = (ios ? Math.Round(Motion.ScrollParameters(.2).Duration * 1000) : 500) * speed;
        double progress = Math.Clamp((time - lineStart) / duration, 0, 1);
        double eased = ios ? Motion.Scroll(progress) : Motion.LegacyScroll(progress);
        return initialY + (floatHeight * fontSize / 41 - initialY) * eased;
    }
    public static double Ease(double progress)
    {
        double x = 1 - Math.Clamp(progress, 0, 1), denominator = G(1);
        double inward = x <= .864 ? Math.Pow(x / .864, 2.5) * .775 / denominator : G(x) / denominator;
        return 1 - inward;
        static double G(double value) => 1 - Math.Pow((1 - value) * 3 / 4 / .136 + .25, 1.84) * .225;
    }
    public static double SyllableDuration
    {
        get
        {
            double damping = 7 / (2 * Math.Sqrt(14));
            return Math.Log(1 / Math.Sqrt(1 - damping * damping) / .001) / 3.5;
        }
    }
    public static double SyllableEase(double progress)
    {
        if (progress <= 0)
            return 0;
        if (progress >= 1)
            return 1;
        double time = progress * SyllableDuration, frequency = Math.Sqrt(14 - 3.5 * 3.5);
        return 1 - Math.Exp(-3.5 * time) * (Math.Cos(frequency * time) + 3.5 / frequency * Math.Sin(frequency * time));
    }
    public static double Y(double time, double start, double duration, double lineStart, double fontSize, bool syllable = false, double floatHeight = 2.75, double speed = 1, bool ios = true, double prepareFromY = 0)
    {
        if (time < start)
            return Prepare(time, lineStart, fontSize, floatHeight, speed, ios, prepareFromY);
        double lift = floatHeight * fontSize / 41, from = Prepare(start, lineStart, fontSize, floatHeight, speed, ios, prepareFromY);
        double p = (time - start) / (syllable ? SyllableDuration * 1000 : 1.76 * Math.Clamp(duration, 900, 1300));
        double eased = syllable ? SyllableEase(p) : Ease(p);
        return from + (-lift - from) * eased;
    }
}
