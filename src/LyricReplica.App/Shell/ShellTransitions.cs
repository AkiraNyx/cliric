using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LyricReplica.App;

public static class ShellTransitions
{
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.RegisterAttached(
        "ReducedMotion", typeof(bool), typeof(ShellTransitions), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SwitchOnProperty = DependencyProperty.RegisterAttached(
        "SwitchOn", typeof(bool), typeof(ShellTransitions), new PropertyMetadata(false, MoveSwitchThumb));
    public static readonly DependencyProperty PlaybackFeedbackProperty = DependencyProperty.RegisterAttached(
        "PlaybackFeedback", typeof(bool), typeof(ShellTransitions), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty PressPoseProperty = DependencyProperty.RegisterAttached(
        "PressPose", typeof(bool), typeof(ShellTransitions), new PropertyMetadata(false, MovePressedContent));
    public static readonly DependencyProperty HoverOpacityProperty = DependencyProperty.RegisterAttached(
        "HoverOpacity", typeof(double), typeof(ShellTransitions), new PropertyMetadata(0d, FadeHoverSurface));

    public static bool GetReducedMotion(DependencyObject element) => (bool)element.GetValue(ReducedMotionProperty);
    public static void SetReducedMotion(DependencyObject element, bool value) => element.SetValue(ReducedMotionProperty, value);
    public static bool GetSwitchOn(DependencyObject element) => (bool)element.GetValue(SwitchOnProperty);
    public static void SetSwitchOn(DependencyObject element, bool value) => element.SetValue(SwitchOnProperty, value);
    public static bool GetPlaybackFeedback(DependencyObject element) => (bool)element.GetValue(PlaybackFeedbackProperty);
    public static void SetPlaybackFeedback(DependencyObject element, bool value) => element.SetValue(PlaybackFeedbackProperty, value);
    public static bool GetPressPose(DependencyObject element) => (bool)element.GetValue(PressPoseProperty);
    public static void SetPressPose(DependencyObject element, bool value) => element.SetValue(PressPoseProperty, value);
    public static double GetHoverOpacity(DependencyObject element) => (double)element.GetValue(HoverOpacityProperty);
    public static void SetHoverOpacity(DependencyObject element, double value) => element.SetValue(HoverOpacityProperty, value);

    private sealed record Transition(AnimationClock Clock, double Destination, Action? Completed);
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, Transition>> Active = new();

    internal static void To(IAnimatable target, DependencyProperty property, double value, MotionTiming timing,
        bool immediate = false, Action? completed = null)
    {
        var element = (DependencyObject)target;
        double current = (double)element.GetValue(property);
        if (immediate || timing.Milliseconds == 0 || Math.Abs(current - value) < .0001)
        {
            Remove(target, property);
            element.SetCurrentValue(property, value);
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(current, value, TimeSpan.FromMilliseconds(timing.Milliseconds))
        {
            EasingFunction = timing.Curve
        };
        var clock = (AnimationClock)animation.CreateClock(true);
        var transition = new Transition(clock, value, completed);
        var properties = Active.GetOrCreateValue(element);
        properties.TryGetValue(property, out var previous);
        properties[property] = transition;
        clock.Completed += (_, _) => Complete(target, property, transition);
        // Replace before removing the old clock so WPF snapshots its visible value.
        target.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
        clock.Controller!.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
        previous?.Clock.Controller?.Remove();
    }

    internal static void Set(IAnimatable target, DependencyProperty property, double value) => To(target, property, value, default);

    internal static void Settle(Dispatcher dispatcher)
    {
        var transitions = Active.Where(entry => entry.Key.Dispatcher == dispatcher)
            .SelectMany(entry => entry.Value.Select(property => (Target: (IAnimatable)entry.Key, Property: property.Key, Run: property.Value)))
            .ToArray();
        foreach (var transition in transitions)
            Complete(transition.Target, transition.Property, transition.Run);
    }

    private static void Complete(IAnimatable target, DependencyProperty property, Transition transition)
    {
        if (Active.TryGetValue((DependencyObject)target, out var properties) &&
            properties.TryGetValue(property, out var pending) && ReferenceEquals(pending, transition))
        {
            Remove(target, property);
            ((DependencyObject)target).SetCurrentValue(property, transition.Destination);
            transition.Completed?.Invoke();
        }
    }

    private static void Remove(IAnimatable target, DependencyProperty property)
    {
        var element = (DependencyObject)target;
        if (!Active.TryGetValue(element, out var properties) || !properties.Remove(property, out var transition))
            return;
        if (properties.Count == 0)
            Active.Remove(element);
        target.BeginAnimation(property, null);
        transition.Clock.Controller!.Remove();
    }

    internal static void Reveal(FrameworkElement element, double opacity, double travel, MotionTiming timing, bool immediate)
    {
        PrepareReveal(element, immediate ? 1 : opacity, immediate ? 0 : travel);
        PlayReveal(element, timing, immediate);
    }

    internal static void PrepareReveal(FrameworkElement element, double opacity, double travel)
    {
        if (travel != 0 || element.RenderTransform is TranslateTransform)
        {
            var shift = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
            element.RenderTransform = shift;
            Set(shift, TranslateTransform.YProperty, travel);
        }
        Set(element, UIElement.OpacityProperty, opacity);
    }

    internal static void PlayReveal(FrameworkElement element, MotionTiming timing, bool immediate)
    {
        if (element.RenderTransform is TranslateTransform shift)
            To(shift, TranslateTransform.YProperty, 0, timing, immediate);
        To(element, UIElement.OpacityProperty, 1, timing, immediate);
    }

    private static void MoveSwitchThumb(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)target;
        var shift = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = shift;
        To(shift, TranslateTransform.XProperty, (bool)e.NewValue ? 18 : 0, MotionTokens.Toggle,
            GetReducedMotion(element) || !element.IsLoaded || !element.IsVisible);
    }

    private static void FadeHoverSurface(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)target;
        double opacity = (double)e.NewValue;
        To(element, UIElement.OpacityProperty, opacity, opacity > (double)e.OldValue ? MotionTokens.HoverIn : MotionTokens.HoverOut,
            GetReducedMotion(element) || !element.IsLoaded || !element.IsVisible);
    }

    private static void MovePressedContent(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)target;
        bool pressed = (bool)e.NewValue;
        bool reduced = GetReducedMotion(element) || !element.IsVisible;
        var scale = element.RenderTransform as ScaleTransform ?? new ScaleTransform();
        element.RenderTransform = scale;
        double value = pressed && !reduced ? (GetPlaybackFeedback(element) ? .9 : .95) : 1;
        var timing = pressed ? MotionTokens.PressIn : MotionTokens.PressOut;
        To(scale, ScaleTransform.ScaleXProperty, value, timing, reduced);
        To(scale, ScaleTransform.ScaleYProperty, value, timing, reduced);
    }
}
