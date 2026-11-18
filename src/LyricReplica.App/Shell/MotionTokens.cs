using System.Windows.Media.Animation;

namespace LyricReplica.App;

internal readonly record struct MotionTiming(int Milliseconds, IEasingFunction? Curve);

internal static class MotionTokens
{
    internal static readonly IEasingFunction Enter = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    internal static readonly IEasingFunction Exit = Freeze(new QuadraticEase { EasingMode = EasingMode.EaseIn });
    internal static readonly IEasingFunction Move = Freeze(new SineEase { EasingMode = EasingMode.EaseInOut });
    private static readonly IEasingFunction Emphasis = Freeze(new PowerEase { Power = 4, EasingMode = EasingMode.EaseOut });

    internal static readonly MotionTiming PageEnter = new(250, Freeze(new QuadraticEase { EasingMode = EasingMode.EaseOut }));
    internal static readonly MotionTiming LyricsPage = new(340, null);
    internal const int LyricsPageStaggerMs = 45;
    internal static readonly MotionTiming PopupEnter = new(200, Enter);
    internal static readonly MotionTiming PopupExit = new(150, Exit);
    internal static readonly MotionTiming PressIn = new(85, Enter);
    internal static readonly MotionTiming PressOut = new(145, Enter);
    internal static readonly MotionTiming HoverIn = new(110, Enter);
    internal static readonly MotionTiming HoverOut = new(145, Enter);
    internal static readonly MotionTiming PlaybackState = new(165, Enter);
    internal static readonly MotionTiming ArtworkReveal = new(155, Enter);
    internal static readonly MotionTiming Toggle = new(160, Move);
    internal static readonly MotionTiming NoticeEnter = new(190, Emphasis);
    internal static readonly MotionTiming NoticeExit = new(135, Exit);
    internal static readonly MotionTiming SeekHover = new(170, Enter);
    internal static readonly MotionTiming SeekRelease = new(185, Enter);

    internal const double PageTravel = 12;
    internal const double PageStartOpacity = .72;
    internal const double PopupTravel = 8;
    internal const double PopupStartOpacity = .32;
    internal const double NoticeTravel = 8;
    internal const int BusyCycleMs = 900;

    internal static T Freeze<T>(T value) where T : System.Windows.Freezable
    {
        value.Freeze();
        return value;
    }
}
