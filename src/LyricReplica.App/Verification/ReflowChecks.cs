using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LyricReplica.Core;

namespace LyricReplica.App;

internal static class ReflowChecks
{
    public static void Run(Action<bool, string> check)
    {
        var line = new LyricLine { Text = "hello world", StartMs = 1000, EndMs = 4200, Words = [new() { Text = "hello ", StartMs = 1000, DurationMs = 1000 }, new() { Text = "world", StartMs = 2200, DurationMs = 2000 }] };
        var original = Create(500, 1);
        original.Update(1000, 1000, true, 0, 1000, 5000, 1);
        original.Update(1050, 1050, true, 0, 1000, 5000, 1);
        var oldMask = (LinearGradientBrush)Strips(original).Single().OpacityMask;
        var oldColors = oldMask.GradientStops.Select(stop => stop.Color).ToArray();
        var split = Create(190, 1);
        split.RestoreWordRun(original);
        split.Update(1050, 1050, true, 0, 1000, 5000, 1);
        var splitStrips = Strips(split);
        check(splitStrips.Length == 2, "reflow fixture actually splits one strip into two");
        check(splitStrips[0].OpacityMask is LinearGradientBrush && splitStrips[1].OpacityMask is null, "new strip without an old mask stays uniformly preplayed until a word dispatch creates its mask");
        check(Math.Abs(splitStrips[1].Opacity - 47d / 255) < 1e-9, "unprepared reflow strip uses the original uniform line opacity");
        var copiedMask = (LinearGradientBrush)splitStrips[0].OpacityMask;
        check(copiedMask.GradientStops.Select(stop => stop.Color).SequenceEqual(oldColors), "new split mask copies the original strip colors");
        var offsets = copiedMask.GradientStops.Select(stop => stop.Offset).ToArray();
        split.Update(1150, 1150, true, 0, 1000, 5000, 1);
        check(copiedMask.GradientStops.Select(stop => stop.Offset).SequenceEqual(offsets) && copiedMask.GradientStops.Select(stop => stop.Color).SequenceEqual(oldColors), "new split masks retain their progress and color snapshot after old clocks are discarded");
        split.Update(2200, 2200, true, 0, 1000, 5000, 1);
        check(splitStrips[1].OpacityMask is LinearGradientBrush && splitStrips[1].Opacity == 1, "the next word recreates its strip mask at full host opacity");
        var resized = Create(600, 1.2);
        resized.RestoreWordRun(original);
        resized.Update(1150, 1150, true, 0, 1000, 5000, 1);
        original.Update(1150, 1150, true, 0, 1000, 5000, 1);
        var sameMask = (LinearGradientBrush)Strips(resized).Single().OpacityMask;
        check(sameMask.GradientStops.Select(stop => stop.Color).SequenceEqual(((LinearGradientBrush)Strips(original).Single().OpacityMask).GradientStops.Select(stop => stop.Color)), "same-strip reflow keeps the existing color clocks advancing");
        check(!sameMask.GradientStops.Select(stop => stop.Color).SequenceEqual(oldColors), "same-strip color animation was not frozen with the split-mask snapshot");
        original.Update(6000, 6000, false, 0, 1000, 5000, 1);
        check(Strips(original).All(strip => strip.OpacityMask is null && Math.Abs(strip.Opacity - 47d / 255) < 1e-9), "completed TextSing stop removes its mask and restores uniform preplay opacity");
        LyricRow Create(double width, double scale)
        {
            var row = new LyricRow(line, false, false, width, scale, new FontFamily("Segoe UI"), new ViewOptions());
            row.Measure(new Size(width, double.PositiveInfinity));
            row.Arrange(new Rect(0, 0, width, row.DesiredSize.Height));
            row.UpdateLayout();
            return row;
        }
    }
    private static Canvas[] Strips(DependencyObject node) => Find(node).OfType<Canvas>().Where(canvas => canvas.Children.OfType<Border>().Any(border => border.Opacity == 1E-5)).ToArray();
    private static IEnumerable<DependencyObject> Find(DependencyObject node)
    {
        yield return node;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        foreach (var child in Find(VisualTreeHelper.GetChild(node, index)))
            yield return child;
    }
}
