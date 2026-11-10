using System.Windows.Controls;
using System.Windows.Media;
using LyricReplica.App.Reference;
using LyricReplica.Core;

namespace LyricReplica.App;

internal static class BreathingPlaybackChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var dots = new BreathingDots();
        var options = new ViewOptions { TimelineOffsetMs = 0 };
        options.AppleMusicLyrics.EnablePlusLighter = true;
        dots.AdvancePlayback(0, (0, 13000), 0, false, 1, "fixture", options);
        check(!dots.Frame.Visible && dots.PlaybackActive, "native breathing consumer keeps the startup delay while paused");
        dots.AdvancePlayback(0, (0, 13000), 500, false, 1, "fixture", options);
        await Task.Delay(600);
        dots.AdvancePlayback(0, (0, 13000), 750, false, 1, "fixture", options);
        check(dots.Frame.Visible && dots.Frame.First > dots.Frame.Second && dots.Frame.Second > dots.Frame.Third && dots.Frame.Scale == 1, "paused consumer runs initial dot fades without dispatching a pulse");
        dots.SamplePlaybackTick(600, true, 800);
        double scale = dots.Frame.Scale;
        dots.AdvancePlayback(600, (0, 13000), 950, false, 1, "fixture", options);
        check(dots.Frame.Scale > scale && dots.Frame.First <= .15, "consumer keeps an already dispatched pulse moving after pause");
        var view = (UserControl)dots.Content;
        var grid = (Grid)view.FindName("GridMain");
        var transform = (ScaleTransform)grid.RenderTransform;
        check(transform.CenterX == .5 && transform.CenterY == .5 && dots.CacheMode is BitmapCache cache && cache.RenderAtScale == 3, "breathing renderer retains the release scale center and PlusLighter bitmap cache");
        dots.AdvancePlayback(3000, (0, 13000), 1000, false, 2, "fixture", options);
        check(!dots.Frame.Visible, "seek generation replaces the old breathing run with a fresh startup wait");
        dots.Dispose();
        await Task.Delay(600);
        check(!dots.Frame.Visible && !dots.PlaybackActive, "late startup callback cannot revive an unloaded or disposed breathing control");
        using var pageDots = new BreathingDots();
        pageDots.AdvancePlayback(2500, (1000, 15000), 0, true, 1, "dispatch", options, false);
        check(!pageDots.PlaybackActive, "manual scrolling suppresses a new breathing run");
        pageDots.AdvancePlayback(2500, (1000, 15000), 10, true, 1, "dispatch", options);
        check(pageDots.PlaybackEndMs == 15000 + Motion.BreathingEndExtensionMs(options.AppleMusicLyrics, true), "ordinary interlude includes the page pulling end compensation");
        pageDots.RestartForLayout();
        pageDots.AdvancePlayback(2500, (1000, 15000), 20, true, 1, "dispatch", options);
        check(pageDots.PlaybackEndMs == 15000, "resize uses the unextended interlude end");
        pageDots.SamplePlaybackTick(2500, true, 520);
        check(pageDots.Frame.Visible, "resize interlude omits the extra 500ms startup delay");
        pageDots.SuspendForManualScroll();
        check(!pageDots.PlaybackActive, "manual scrolling stops an active breathing run");
    }
}
