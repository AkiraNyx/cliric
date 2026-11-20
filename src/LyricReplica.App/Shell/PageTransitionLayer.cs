using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LyricReplica.App;

internal sealed class PageTransitionLayer : Grid
{
    private static readonly BitmapCache BlurCache = MotionTokens.Freeze(new BitmapCache { RenderAtScale = .5 });
    private readonly BlurEffect _blur = new() { RenderingBias = RenderingBias.Performance };
    private readonly Border _surface;

    internal double Amount { get; private set; }
    internal bool UsesCachedBlur => CacheMode is not null;

    internal PageTransitionLayer(FrameworkElement page)
    {
        _surface = new Border { Child = page };
        Children.Add(_surface);
    }

    internal void SetPose(double amount)
    {
        Amount = amount;
        Opacity = amount;
        // Half-DIP steps keep the low-resolution surface reusable between blur changes.
        double radius = Math.Round(12 * (1 - amount)) / 2;
        bool soften = amount > .004 && radius > 0;
        if (soften)
            _blur.Radius = radius;
        if (soften != UsesCachedBlur)
        {
            _surface.Effect = soften ? _blur : null;
            CacheMode = soften ? BlurCache : null;
        }
    }
}
