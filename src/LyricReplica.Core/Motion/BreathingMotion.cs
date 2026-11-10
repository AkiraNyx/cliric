namespace LyricReplica.Core;

public readonly record struct BreathingFrame(bool Visible, double Scale, double Opacity, double First, double Second, double Third);

public static class BreathingMotion
{
    public static (double Scale, double Opacity, bool Finished) Exit(double elapsed, double from, bool tremendous = false)
    {
        double peak = tremendous ? 5 : 1.13;
        if (elapsed < 975)
            return (Lerp(from, peak, 1 - Math.Pow(1 - Fraction(elapsed, 900), 2.2)), 1, false);
        if (!tremendous)
        {
            double p = Fraction(elapsed - 975, 267);
            return (Lerp(peak, .85, Math.Pow(p, 2.7)), 1 - p, elapsed >= 1275);
        }
        if (elapsed < 1375)
            return (Lerp(5, .75, Quadratic(Fraction(elapsed - 975, 400))), 1, false);
        if (elapsed < 1475)
            return (Lerp(.75, 1.2, Math.Pow(Fraction(elapsed - 1375, 150), 2.7)), 1, false);
        double interrupted = Lerp(.75, 1.2, Math.Pow(100d / 150, 2.7)), last = Fraction(elapsed - 1475, 200);
        return (Lerp(interrupted, .5, Math.Pow(last, 2.7)), 1 - last, elapsed >= 1675);
        static double Fraction(double elapsed, double duration) => Math.Clamp(elapsed / duration, 0, 1);
        static double Lerp(double from, double to, double p) => from + (to - from) * p;
    }
    internal static double Quadratic(double p) => p < .5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2;
    public static BreathingFrame At(double position, double start, double end, bool delayed, bool reduceMotion = false)
    {
        double begin = start + (delayed ? 1000 : 500), duration = end - begin, t = position - begin;
        if (t < 0 || position > end || duration <= 1260)
            return new(false, 1, 0, 0, 0, 0);
        int cycles = Math.Max(1, (int)((duration - 1260) / 5500 + .25));
        int period = (int)((duration - 1260) / cycles), dotPeriod = (int)((duration - 2360) / 3);
        double exit = cycles * period, scale = Pulse(Math.Min(t, exit)), opacity = 1;
        if (t >= exit)
        {
            double elapsed = t - exit, expanded = scale + (1.13 - scale) * (1 - Math.Pow(1 - Math.Clamp(elapsed / 900, 0, 1), 2.2));
            scale = expanded;
            if (elapsed >= 975)
            {
                double p = Math.Clamp((elapsed - 975) / 267, 0, 1);
                scale = 1.13 + (.85 - 1.13) * Math.Pow(p, 2.7);
                opacity = 1 - p;
            }
        }
        return new(opacity > 0, reduceMotion ? 1 : scale, opacity, Dot(0), Dot(1), Dot(2));
        double Pulse(double at)
        {
            double half = period / 2d;
            int step = Math.Min(cycles * 2 - 1, (int)(Math.Max(0, at - 1e-6) / half));
            double from = step % 2 == 0 ? .8 : 1.1, to = step % 2 == 0 ? 1.1 : .8, p = Math.Clamp((at - step * half) / (period / 2.05), 0, 1);
            double eased = p < .5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2;
            return from + (to - from) * eased;
        }
        double Dot(int index)
        {
            double initial = .15 * Math.Clamp((t - 84 * index) / 750, 0, 1);
            double p = Math.Clamp((t - 1200 - dotPeriod * index) / Math.Max(1, dotPeriod * .8), 0, 1);
            return initial + (1 - initial) * p;
        }
    }
}
