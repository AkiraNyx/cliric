namespace LyricReplica.Core;

public readonly record struct MarqueeTiming(double Width, double From, double To, double EnterPosition, double CruisePosition, double Delay, double EnterEnd, double CruiseEnd, double End)
{
    public static MarqueeTiming Create(double width, double delayMs)
    {
        double travel = width + 45, brake = Math.Clamp(travel * .4, 48, Math.Min(120, travel));
        double cruise = Math.Max(travel - 7.15 - brake, 0), delay = Math.Max(0, delayMs);
        return new(width, 16, 16 - travel, 8.85, 8.85 - cruise, delay, delay + 330, delay + 330 + cruise / 65 * 1000, delay + 330 + cruise / 65 * 1000 + brake / 65 * 3000);
    }
}
