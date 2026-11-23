using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Reflection;
using Cliric.App.Reference;
using Cliric.Core;

namespace Cliric.App;

static class MagicStripChecks
{
    public static async Task RunDynamicAsync(Action<bool, string> check)
    {
        var page = new ReferencePageView(new FontFamily("Segoe UI"), true);
        var settings = new AppleMusicSettings { MagicStripTutorial = true };
        long pointerNow = 1000;
        using var controller = new MagicStripController(page, () => false, () => false, _ => { }, () => settings, () => { }, () => pointerNow);
        var tip = page.Get<TextBlock>("TextBlockMagicButtonTip1");
        var bar = page.Get<Border>("BorderMagicButtonInside");
        page.View.Measure(new Size(1100, 713));
        page.View.Arrange(new Rect(0, 0, 1100, 713));
        page.View.UpdateLayout();
        controller.BeginTutorial(0);
        controller.AdvanceTutorial(1500);
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(80);
            Sample();
        }
        check(tip.FontSize > 17.9 && bar.Width > 40, "unreduced tutorial entry reaches its expanded text and bar through actual WPF clocks");
        controller.AdvanceTutorial(4500);
        for (int i = 0; i < 6; i++)
        {
            await Task.Delay(80);
            Sample();
        }
        check(Math.Abs(tip.FontSize - .01) < .001 && Math.Abs(bar.Width - 40) < .1, "unreduced tutorial collapse reaches its small text and bar through actual WPF clocks");
        settings.MagicStripTutorial = false;
        controller.AdvanceTutorial(5000);
        controller.Preview(true, MagicAction.Close);
        var inside = page.Get<Grid>("GridMagicButtonInside");
        controller.MovePointer(new Point(inside.ActualWidth / 2 + 30, inside.ActualHeight / 2 - 15));
        var entry = MarginPlan();
        check(entry is not null && entry.Duration.TimeSpan.TotalMilliseconds <= 350 && entry.To == new Thickness(20, -10, -20, 10) && entry.EasingFunction is BackEase { Amplitude: .7 }, "Magic Strip entry pointer uses the original symmetric margin target and initial back-ease window");
        pointerNow += 200;
        controller.MovePointer(new Point(inside.ActualWidth / 2 + 60, inside.ActualHeight / 2 + 30));
        var remaining = MarginPlan();
        check(remaining is null || remaining.Duration.TimeSpan.TotalMilliseconds <= 150, "pointer moves consume the original remaining entry time instead of restarting 350ms");
        pointerNow += 170;
        controller.MovePointer(new Point(inside.ActualWidth / 2 + 45, inside.ActualHeight / 2 - 30));
        page.View.UpdateLayout();
        check(MarginPlan() is null && bar.Margin == new Thickness(30, -20, -30, 20), "after entry the bar follows the pointer directly without an animation tail");
        check(inside.RenderTransform.Value.IsIdentity && ((Border)inside.Children[2]).Margin == bar.Margin, "Magic Strip keeps its hit grid fixed while the existing icon margin binding follows the bar");
        controller.Preview(false, MagicAction.None);
        var returning = MarginPlan();
        check(returning is not null && returning.Duration.TimeSpan.TotalMilliseconds == 500 && returning.To == new Thickness(0) && returning.EasingFunction is BackEase { Amplitude: .25 }, "leaving retains the original 500ms return and amplitude");
        controller.MovePointer(new Point(0, 0));
        check(ReferenceEquals(returning, MarginPlan()), "inactive pointer movement cannot restart tracking after leave or release");
        controller.BeginGesture(new Point(0, 0), false);
        controller.MovePointer(new Point(70, 0));
        check(controller.PreviewAction == MagicAction.Maximize, "a captured drag still selects its gesture outside the hover tracking window");
        controller.BeginGesture(new Point(0, 0), true);
        controller.MovePointer(new Point(0, -70));
        check(controller.PreviewAction == MagicAction.Top, "secondary captured drag retains lyric navigation");
        ThicknessAnimation? MarginPlan()
        {
            var storageType = typeof(AnimationTimeline).Assembly.GetType("System.Windows.Media.Animation.AnimationStorage")!;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var storage = storageType.GetMethod("GetStorage", flags)!.Invoke(null, [bar, FrameworkElement.MarginProperty]);
            if (storage is null)
                return null;
            var clocks = storageType.GetField("_animationClocks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(storage)!;
            return (ThicknessAnimation)((AnimationClock)clocks.GetType().GetProperty("Item")!.GetValue(clocks, [0])!).Timeline;
        }
        void Sample()
        {
            page.View.UpdateLayout();
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap(1100, 713, 96, 96, PixelFormats.Pbgra32);
            image.Render(page.View);
            check(double.IsFinite(tip.FontSize) && tip.FontSize > 0 && double.IsFinite(bar.Width) && bar.Width > 0, "tutorial clock frame keeps legal text and bar dimensions");
        }
    }
    public static void Run(Action<bool, string> check)
    {
        foreach (var item in new (bool? Preference, int Count, bool Enabled, bool? Saved, int Remaining)[] { (null, 5, true, null, 4), (null, 1, true, null, 0), (null, 0, false, false, 0), (null, -1, false, false, -1), (true, 0, true, true, 0), (false, 5, false, false, 5) })
        {
            var config = new AppleMusicSettings { MagicStripTutorial = item.Preference, MagicStripTutorialCount = item.Count };
            check(config.BeginMagicStripTutorial() == item.Enabled && config.MagicStripTutorial == item.Saved && config.MagicStripTutorialCount == item.Remaining, "tutorial entry matches the recovered Loaded preference/count branch " + item);
        }
        var page = new ReferencePageView(new FontFamily("Segoe UI"), true);
        var settings = new AppleMusicSettings();
        int saves = 0;
        using var controller = new MagicStripController(page, () => false, () => true, _ => { }, () => settings, () => saves++);
        var tips = Enumerable.Range(1, 4).Select(i => page.Get<TextBlock>("TextBlockMagicButtonTip" + i)).ToArray();
        var bar = page.Get<Border>("BorderMagicButtonInside");
        var notice = page.Get<Grid>("GridMagicStripFeatureNotice");
        controller.BeginTutorial(0);
        check(controller.TutorialRunning && settings.MagicStripTutorialCount == 4 && saves == 1, "automatic tutorial consumes and saves one entry allowance");
        controller.AdvanceTutorial(1499);
        check(tips.All(t => t.Visibility == Visibility.Collapsed), "tutorial waits the recovered 1500 ms before showing its first tip");
        for (int i = 0; i < 4; i++)
        {
            controller.AdvanceTutorial(1500 + i * 3350);
            check(tips[i].Visibility == Visibility.Visible && tips[i].Opacity == 1 && tips[i].FontSize == 18 && tips.Count(t => t.Visibility == Visibility.Visible) == 1 && bar.Width > 40 && bar.Height > 8, "tutorial enters source tip " + (i + 1));
            double width = bar.Width;
            controller.Preview(true, MagicAction.Close);
            check(bar.Width == width, "hover preserves the running tutorial geometry " + (i + 1));
            controller.AdvanceTutorial(4500 + i * 3350);
            check(tips[i].Opacity == 0 && tips[i].FontSize == .01 && bar.Width == 40 && bar.Height == 8, "tutorial returns its bar after the recovered 3000 ms tip interval " + (i + 1));
        }
        controller.AdvanceTutorial(14950);
        check(!controller.TutorialRunning && tips.All(t => t.Visibility == Visibility.Collapsed), "last tutorial tip closes after 400 ms and the scheduler finishes");
        settings.MagicStripStatus = false;
        controller.AdvanceTutorial(15000);
        check(notice.Visibility == Visibility.Collapsed && notice.Opacity == 0, "disabled MagicStripStatus removes the feature notice");
        settings.MagicStripStatus = true;
        controller.AdvanceTutorial(15001);
        check(notice.Visibility == Visibility.Visible, "feature notice can be reenabled without rebuilding the page");
        settings.MagicStripTutorial = true;
        controller.AdvanceTutorial(16000);
        controller.AdvanceTutorial(17500);
        check(controller.TutorialRunning && tips[0].Visibility == Visibility.Visible && settings.MagicStripTutorialCount == 4 && saves == 1, "explicit tutorial preference restarts the presentation without consuming automatic entries");
        settings.MagicStripTutorial = false;
        controller.AdvanceTutorial(17501);
        check(!controller.TutorialRunning && tips.All(t => t.Visibility == Visibility.Collapsed) && bar.Width == 40, "disabling tutorial cancels active presentation and restores its bar");
        settings.MagicStripTutorial = true;
        controller.AdvanceTutorial(18000);
        controller.AdvanceTutorial(19500);
        controller.Dispose();
        controller.AdvanceTutorial(25000);
        check(!controller.TutorialRunning && tips.All(t => t.Visibility == Visibility.Collapsed), "disposed tutorial cannot resume on a later frame");
        check(PresentationSource.FromVisual(page.View) is null, "Magic Strip checks create no native window");
    }
}
