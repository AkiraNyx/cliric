using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LyricReplica.Core;
using Timeline = LyricReplica.Core.Timeline;

namespace LyricReplica.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<FrameworkElement, double> _pullLayout = [];
    private double _lastManualScroll = double.NegativeInfinity, _lastPullStarted = double.NegativeInfinity, _lastPullDuration;
    private (bool UseBackEase, double GapSeconds) _scrollTransition;
    private int PullCount => _rows.Count + (_credit is null ? 0 : 1);
    private FrameworkElement PullNode(int index) => index < _rows.Count ? _rows[index].Host : _credit!;
    private TranslateTransform PullTransform(int index)
    {
        if (index < _rows.Count)
            return _rows[index].Pull;
        if (_credit!.RenderTransform is not TranslateTransform)
            _credit.RenderTransform = new TranslateTransform();
        return (TranslateTransform)_credit.RenderTransform;
    }
    private double PullY(int index) => VisualTreeHelper.GetOffset(PullNode(index)).Y;
    private void CapturePullLayout()
    {
        _pullLayout.Clear();
        for (int i = 0; i < PullCount; i++)
            _pullLayout[PullNode(i)] = LayoutInformation.GetLayoutSlot(PullNode(i)).Y;
    }
    private double PullLayoutDelta(int index) => _pullLayout.TryGetValue(PullNode(index), out double before) ? LayoutInformation.GetLayoutSlot(PullNode(index)).Y - before : 0;
    private int AboveCount(int line, bool skipLast = true)
    {
        double top = _scroll.VerticalOffset - _linesPanel.Margin.Top;
        int count = 0;
        for (int i = line - 1; i >= 0; i--)
        {
            if (PullY(i) - top <= (skipLast ? 0 : 60 * _displayScale))
                break;
            count++;
        }
        return skipLast && count > 0 ? count - 1 : count;
    }
    private int FirstAtOffset(double offset)
    {
        int first = 0;
        double top = offset - _linesPanel.Margin.Top;
        while (first < PullCount && PullY(first) + PullNode(first).ActualHeight <= top)
            first++;
        return first;
    }
    private int PortraitPullStart(int line, double target)
    {
        double local = _linesPanel.Margin.Top + PullY(line) - target;
        double top = _scroll.VerticalOffset - _linesPanel.Margin.Top + local - 2;
        int first = line;
        while (first > 0 && PullY(first - 1) + PullNode(first - 1).ActualHeight > top)
            first--;
        return first;
    }
    private int LandscapeAboveCount(int line)
    {
        double top = _scroll.VerticalOffset - _linesPanel.Margin.Top;
        if (PullY(line) > top + _scroll.ActualHeight + 10 * _displayScale)
            return 0;
        int count = 0;
        for (int i = line - 2; i >= 0; i--)
        {
            if (PullY(i) - top < 30 * _displayScale)
                break;
            count++;
        }
        return count;
    }
    private double PullingSpan(double target, int line)
    {
        var settings = _options.AppleMusicLyrics;
        int count = 0;
        if (line + 1 >= _groups.Count)
            return Unit();
        for (int i = line; i < PullCount; i++)
        {
            if (PullY(i) - _scroll.ActualHeight - _linesPanel.Margin.Top > target)
                break;
            count++;
        }
        bool above = false;
        for (int i = line - 1; i >= 0; i--)
        {
            if (PullY(i) - _scroll.VerticalOffset + _linesPanel.Margin.Top <= -10 * _displayScale)
                break;
            count++;
            above = true;
        }
        return (count - (above ? 1 : 0)) * Unit();
        double Unit() => (settings.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS ? 1d / 31 : .03) * settings.PullingDelay * settings.AnimationSpeed;
    }
    private double PullSpeed(int line, double target, bool switching)
    {
        double total = PullingSpan(target, line) * (switching ? 620 : 1) + Motion.ScrollDurationMs(_options.AppleMusicLyrics, _pageKaraoke) * _options.AnimationSpeed;
        if (line + 1 >= _groups.Count)
            return 1;
        double interval = _groups[line + 1].StartMs - _groups[line].StartMs;
        return total > interval - 300 ? Math.Max(0, interval / (total + 350)) : 1;
    }
    private bool RecentManualInView(int line, double now) => now - _lastManualScroll <= .5 && IsLyricLineInView(line);

    private bool IsLyricLineInView(int line)
    {
        if (line < 0 || line >= _rows.Count)
            return false;
        double top = _scroll.VerticalOffset - _linesPanel.Margin.Top, y = PullY(line);
        return y + PullNode(line).ActualHeight >= top + (_portrait ? 60 * _displayScale : 0) && y <= top + _scroll.ActualHeight - (_portrait && _verticalControlsShown ? _verticalControls.ActualHeight : 0);
    }
    private void ConfigureSwitchDelays(int anchor, double time, double now)
    {
        if (anchor < 0 || anchor >= _rows.Count)
            return;
        bool pulling = _options.AppleMusicLyrics.PullingEffect && !_options.ReduceMotion && !RecentManualInView(anchor, now);
        double target = PullY(anchor) + 465 * _displayScale - 130, m = PullSpeed(anchor, target, true);
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            bool active = Timeline.Active(row.Data, time);
            if (row.Main.SlotOpen == active)
                continue;
            int delay = 0;
            if (pulling)
            {
                int above = _portrait ? Math.Max(0, anchor - PortraitPullStart(anchor, target) - (active ? 0 : anchor - i - 1)) : AboveCount(anchor, false) - 1 - (active ? 0 : anchor - i - 1);
                if (active && i == anchor && i > 0 && row.Data.StartMs - _groups[i - 1].EndMs <= 6000)
                    delay = Delay(1, m) + Delay(above, m);
                else if (!active && (i + 1 >= _groups.Count || _groups[i + 1].StartMs - row.Data.EndMs <= 6000))
                    delay = Delay(above, m);
                else if (!active && i == anchor && now >= _manualUntil && i + 1 < _groups.Count && _groups[i + 1].StartMs - row.Data.EndMs > 6000 && !_dots.PlaybackActive)
                    delay = Delay(1, 1);
            }
            double effective = Math.Max(0, delay) * (_options.AppleMusicLyrics.InterLineAnimationPreset == InterLineAnimationPresetTypes.Lyricify && row.Main.OpacityTransition.UseBackEase ? 1.66 : 1);
            row.Main.SwitchDelayMs = effective;
            foreach (var background in row.Backgrounds)
                background.SwitchDelayMs = effective;
        }
        int Delay(int distance, double speed) => (int)(620 * speed * Math.Max(0, distance) * (_options.AppleMusicLyrics.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS ? 1d / 31 : .03) * _options.AppleMusicLyrics.PullingDelay * _options.AnimationSpeed);
    }
    private void ApplyPulling(double target, int line, bool breathing, (bool UseBackEase, double GapSeconds) transition)
    {
        var settings = _options.AppleMusicLyrics;
        bool ios = settings.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS;
        double m = 1;
        int startLine = breathing ? 1 : 0;
        if (!breathing && settings.PullingEffectNew && _wall.Elapsed.TotalMilliseconds - _lastPullStarted >= _lastPullDuration)
        {
            m = PullSpeed(line, target, false);
            _lastPullDuration = PullingSpan(target, line) + Motion.ScrollDurationMs(settings, _pageKaraoke) * settings.AnimationSpeed;
            if (!_portrait)
                startLine = -(AboveCount(line, false) - 1);
            if (line > 0 && _groups[line].StartMs - _groups[line - 1].EndMs > 6000 || line == 0 && _groups[0].StartMs > 6500)
                startLine = 1;
        }
        _lastPullStarted = _wall.Elapsed.TotalMilliseconds;
        int duration = (int)(Motion.ScrollDurationMs(settings, _pageKaraoke) * settings.AnimationSpeed * m);
        if (transition.UseBackEase)
            duration = (int)(duration * (ios ? Motion.ScrollParameters(transition.GapSeconds).Duration / Motion.ScrollParameters().Duration : 2.26));
        int step = (int)(Motion.PullingDelayMs(1, settings.AnimationSpeed, settings.PullingDelay, ios, transition.UseBackEase) * m);
        double background = 0;
        if (_rows[line].Backgrounds.Count > 0)
        {
            if (startLine >= 0)
                startLine = 1;
            background += _rows[line].Backgrounds.Sum(b => b.ActualHeight);
        }
        if (line > 0 && _groups[line].EndMs == _groups[line - 1].EndMs && _rows[line - 1].Backgrounds.Count > 0)
        {
            if (startLine >= 0)
                startLine = 1;
            background += _rows[line - 1].Backgrounds.Sum(b => b.ActualHeight + b.Margin.Top);
        }
        double offset = _scroll.VerticalOffset, delta = target - offset;
        bool compensateOnly = !breathing && ((!settings.BackgroundVocalsEnterAnimationNew && Math.Abs(delta - background) <= 10) || Math.Abs(delta) < 2);
        int first = Math.Max(0, line + startLine), relative = startLine, older = -1;
        if (!breathing)
        {
            first = Math.Min(first, _portrait ? PortraitPullStart(line, target) : Math.Max(0, line - 2 - LandscapeAboveCount(line)));
            if (delta < 0)
                first = Math.Min(first, FirstAtOffset(target));
            if (_portrait)
            {
                relative = first - line;
                older = Math.Min(first, FirstAtOffset(offset));
            }
        }
        int end = first;
        while (end < PullCount && PullY(end) - _scroll.ActualHeight - _linesPanel.Margin.Top <= target)
            end++;
        _autoScrolling = false;
        _scroll.ScrollToVerticalOffset(target);
        if (compensateOnly)
        {
            for (int i = line + 1; i < end; i++)
            if (Math.Abs(PullLayoutDelta(i)) >= .1)
                AnimatePull(i, -PullLayoutDelta(i), 0, duration, Math.Max(0, i - line) * step, transition);
            return;
        }
        if (breathing)
            for (int i = line, count = AboveCount(line) + 2; i >= 0 && line - i <= count; i--)
                AnimatePull(i, delta - PullLayoutDelta(i), -83 * _displayScale, duration, 0, transition);
        for (int i = older; i >= 0 && i < first; i++)
            AnimatePull(i, delta - PullLayoutDelta(i), 0, duration, step, transition);
        for (int i = first; i < end; i++)
        {
            int delay = (int)(620 * m * (i - line + 1 - relative) * (ios ? 1d / 31 : .03) * settings.PullingDelay * settings.AnimationSpeed * (transition.UseBackEase && !ios ? 1.23 : 1)) + (breathing ? step : 0);
            bool overlap = _rows[line].Main.SlotOpen && _rows[line].Backgrounds.Any(back => back.BackgroundOverlap);
            AnimatePull(i, delta - (overlap || breathing && _rows[line].Backgrounds.Count > 0 ? 0 : PullLayoutDelta(i)), 0, duration, Math.Max(0, delay), transition);
        }
    }
    private double ScrollEase(double p, (bool UseBackEase, double GapSeconds) transition) => _options.AppleMusicLyrics.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS ? Motion.Scroll(p, transition.UseBackEase ? transition.GapSeconds : null) : transition.UseBackEase ? Motion.LegacyBackScroll(p) : Motion.LegacyScroll(p);
    private void AnimatePull(int index, double compensation, double target, double duration, int delay, (bool UseBackEase, double GapSeconds) transition)
    {
        var move = PullTransform(index);
        double from = move.Y + compensation;
        move.BeginAnimation(TranslateTransform.YProperty, null);
        move.Y = from;
        if (_options.ReduceMotion)
        {
            move.Y = target;
            return;
        }
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(Math.Max(0, duration))) { BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, delay)), EasingFunction = new PageScrollEase(_options.AppleMusicLyrics.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS, transition) { EasingMode = EasingMode.EaseIn } });
    }
    private sealed class PageScrollEase(bool ios, (bool UseBackEase, double GapSeconds) transition) : EasingFunctionBase
    {
        protected override double EaseInCore(double p) => ios ? Motion.Scroll(p, transition.UseBackEase ? transition.GapSeconds : null) : transition.UseBackEase ? Motion.LegacyBackScroll(p) : Motion.LegacyScroll(p);
        protected override Freezable CreateInstanceCore() => new PageScrollEase(ios, transition);
    }
    private void CheckPageMotion(Action<bool, string> check)
    {
        bool reduced = _options.ReduceMotion;
        double offset = _scroll.VerticalOffset;
        var settings = _options.AppleMusicLyrics;
        bool pulling = settings.PullingEffect;
        try
        {
            _options.ReduceMotion = false;
            settings.PullingEffect = true;
            CapturePullLayout();
            var transition = (UseBackEase: false, GapSeconds: .2);
            double target = offset + 120;
            ApplyPulling(target, 0, false, transition);
            _scroll.UpdateLayout();
            check(!_autoScrolling && Math.Abs(_scroll.VerticalOffset - target) < 1, "pulling scroll moves the viewport directly to the target");
            check(_rows[0].Pull.HasAnimatedProperties && Math.Abs((double)_rows[0].Pull.GetAnimationBaseValue(TranslateTransform.YProperty) - 120) < 1, "per-row compensation preserves the screen position after a viewport jump");
            check(_rows.Skip(1).Any(row => row.Pull.HasAnimatedProperties), "pulling schedules the other visible rows instead of only the active line");
            ManualScroll();
            check(_rows.All(row => Math.Abs((double)row.Pull.GetAnimationBaseValue(TranslateTransform.YProperty) - row.Pull.Y) < 1), "manual scrolling replaces pending pulling clocks from their current snapshots");
        }
        finally
        {
            _options.ReduceMotion = reduced;
            settings.PullingEffect = pulling;
            _lastManualScroll = double.NegativeInfinity;
            _manualUntil = 0;
            for (int i = 0; i < PullCount; i++)
            {
                var move = PullTransform(i);
                move.BeginAnimation(TranslateTransform.YProperty, null);
                move.Y = 0;
            }
            _scroll.ScrollToVerticalOffset(offset);
            _autoScrolling = false;
        }
    }
}
