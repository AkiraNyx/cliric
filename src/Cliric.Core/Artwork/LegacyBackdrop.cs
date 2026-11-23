namespace Cliric.Core;

public readonly record struct BackdropLayer(double ScaleX, double ScaleY, double X, double Y, double Angle);

public static class LegacyBackdrop
{
    public static (BackdropLayer First, BackdropLayer Second) At(double seconds, double firstAngle, double secondAngle)
    {
        seconds = Math.Max(0, seconds);
        return (new(Ping(1.1, 2.6, 25), Ping(.9, 1.8, 40), Ping(-160, 170, 50), Ping(-160, 170, 25), firstAngle + seconds / 90 * 360),
            new(Ping(3.5, 2.7, 40), Ping(4, 2, 70), Ping(420, -420, 36), Ping(420, -420, 45), secondAngle - seconds / 70 * 360));
        double Ping(double from, double to, double duration)
        {
            double p = seconds / duration % 2;
            if (p > 1)
                p = 2 - p;
            return from + (to - from) * p;
        }
    }
}
