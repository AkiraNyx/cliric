namespace Cliric.Core;

public readonly record struct TimerSlot(char Value, double DelayMs, bool Visible, bool Changed);

public sealed class TimerPlan
{
    private readonly char[] _values = [' ', ' ', ' ', ' ', ' ', ' '];
    private readonly double[] _delays = new double[6];
    public IReadOnlyList<TimerSlot> Update(double milliseconds)
    {
        int minutes = (int)Math.Abs(milliseconds / 60000), seconds = (int)(Math.Abs(milliseconds) / 1000) % 60;
        char[] next = [milliseconds < 0 ? '-' : ' ', minutes / 10 == 0 ? ' ' : (char)(minutes / 10 + '0'), (char)(minutes % 10 + '0'), ':', (char)(seconds / 10 + '0'), (char)(seconds % 10 + '0')];
        var result = new TimerSlot[6];
        double delay = 0, step = 75;
        for (int i = 0; i < 6; i++)
        {
            if (i == 4 && delay == 0)
                step = 110;
            bool changed = i == 3 ? delay > 0 : delay > 0 || _values[i] != next[i];
            if (changed)
            {
                _delays[i] = delay;
                _values[i] = next[i];
                delay += step;
            }
            bool visible = i == 0 ? milliseconds < 0 : i == 1 ? minutes >= 10 : true;
            result[i] = new(_values[i], _delays[i], visible, changed);
        }
        return result;
    }
}
