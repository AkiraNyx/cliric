using System.Windows;

namespace Cliric.App;

internal static class SleepPreventionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var calls = new List<uint>();
        var prevention = new SleepPrevention(flags => { calls.Add(flags); return SleepPrevention.Continuous; });
        prevention.Update(false, false, WindowState.Normal);
        prevention.Update(true, true, WindowState.Normal);
        check(calls.Count == 0 && !prevention.IsHeld, "sleep setting does not request a hold for an unloaded page or normal fullscreen-only window");
        prevention.Update(true, true, WindowState.Maximized);
        check(prevention.IsHeld && calls.SequenceEqual(new[] { 0x80000003u }), "maximized page requests the original continuous system/display flags");
        prevention.Update(true, true, WindowState.Minimized);
        check(calls.Count == 1, "minimizing retains the reference page's last maximized flag");
        prevention.Update(true, true, WindowState.Normal);
        check(!prevention.IsHeld && calls[^1] == 0x80000000, "returning to a normal fullscreen-only window restores the thread request");
        prevention.Update(true, false, WindowState.Normal);
        prevention.Update(true, false, WindowState.Minimized);
        check(prevention.IsHeld && calls.Count == 3, "page-open policy holds while paused or minimized without repeating the native call");
        prevention.Update(true, true, WindowState.Normal);
        check(!prevention.IsHeld && calls.Count == 4, "changing the setting to fullscreen-only releases the current normal-window hold");
        prevention.Update(true, false, WindowState.Normal);
        prevention.Update(false, false, WindowState.Normal);
        check(!prevention.IsHeld && calls.Count == 6, "page unload restores its thread even when the page-open policy is enabled");
        prevention.Update(true, true, WindowState.Maximized);
        prevention.Release();
        int released = calls.Count;
        prevention.Release();
        check(!prevention.IsHeld && calls.Count == released && calls[^1] == SleepPrevention.Continuous, "closing releases a hold once and repeated cleanup is harmless");
        bool succeed = false;
        int attempts = 0;
        var failing = new SleepPrevention(_ => { attempts++; return succeed ? SleepPrevention.Continuous : 0; });
        check(!failing.Update(true, false, WindowState.Normal) && !failing.IsHeld, "failed execution-state request is not recorded as an active hold");
        succeed = true;
        check(failing.Update(true, false, WindowState.Normal) && failing.IsHeld && attempts == 2, "failed request can retry on the next lifecycle change");
        succeed = false;
        check(!failing.Release() && failing.IsHeld, "failed restore retains its state so close can retry");
        succeed = true;
        check(failing.Release() && !failing.IsHeld, "a later restore releases the previously failed hold");
        int before = calls.Count;
        bool refused = Task.Run(() => { try { prevention.Update(true, false, WindowState.Normal); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
        check(refused && calls.Count == before, "cross-thread changes are rejected before touching the thread-scoped native API");
    }
}
