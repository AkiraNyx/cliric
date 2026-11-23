namespace Cliric.Core;

public enum MagicAction
{
    None, Close, Maximize, Restore, Minimize, Cancel, Top, Bottom, Current
}

public static class MagicGesture
{
    public static MagicAction Select(double x, double y, bool secondary, bool maximized)
    {
        if (Math.Abs(x) >= Math.Abs(y))
        {
            if (Math.Abs(x) < 50)
                return secondary ? MagicAction.None : MagicAction.Close;
            return secondary ? MagicAction.Current : maximized ? MagicAction.Restore : MagicAction.Maximize;
        }
        if (secondary)
            return y <= -50 ? MagicAction.Top : y >= 50 ? MagicAction.Bottom : MagicAction.None;
        return y <= -5 ? MagicAction.Cancel : y >= 50 ? MagicAction.Minimize : MagicAction.Close;
    }
}
