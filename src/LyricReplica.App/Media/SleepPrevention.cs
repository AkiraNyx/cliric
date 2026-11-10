using System.Runtime.InteropServices;
using System.Windows;

namespace LyricReplica.App;

internal sealed class SleepPrevention
{
    internal const uint Continuous = 0x80000000, Prevent = Continuous | 3;
    private readonly Func<uint, uint> _setState;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _maximized;
    internal bool IsHeld
    {
        get; private set;
    }
    internal SleepPrevention() : this(SetThreadExecutionState) { }
    internal SleepPrevention(Func<uint, uint> setState) => _setState = setState;
    internal bool Update(bool pageLoaded, bool fullscreenOnly, WindowState state)
    {
        VerifyThread();
        if (state != WindowState.Minimized)
            _maximized = state == WindowState.Maximized;
        return Apply(pageLoaded && (!fullscreenOnly || _maximized));
    }
    internal bool Release()
    {
        VerifyThread();
        return Apply(false);
    }
    private bool Apply(bool hold)
    {
        if (hold == IsHeld)
            return true;
        if (_setState(hold ? Prevent : Continuous) == 0)
            return false;
        IsHeld = hold;
        return true;
    }
    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Sleep prevention must use its owning UI thread.");
    }
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint SetThreadExecutionState(uint flags);
}
