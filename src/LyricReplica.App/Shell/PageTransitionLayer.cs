using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LyricReplica.App;

internal sealed class PageTransitionLayer : Grid
{
    private static readonly BitmapCache SharpCache = MotionTokens.Freeze(new BitmapCache());
    private static readonly BitmapCache BlurCache = MotionTokens.Freeze(new BitmapCache { RenderAtScale = .5 });
    private static readonly BlurEffect Gaussian = MotionTokens.Freeze(new BlurEffect { Radius = 6, RenderingBias = RenderingBias.Performance });
    private readonly FrameworkElement _page;
    private readonly Border _sharp, _blurred;
    private readonly VisualBrush _source = new() { AutoLayoutContent = false, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };

    internal double Amount { get; private set; }
    internal bool UsesCachedBlur => _source.Visual is not null;

    internal PageTransitionLayer(FrameworkElement page)
    {
        _page = page;
        _sharp = new Border { Child = page };
        // Cache the filtered result on its parent so fading it doesn't rerun the Gaussian.
        _blurred = new Border
        {
            CacheMode = BlurCache,
            Child = new Border { Background = _source, Effect = Gaussian },
            IsHitTestVisible = false,
            Visibility = Visibility.Hidden
        };
        page.SizeChanged += (_, e) => _source.Viewbox = new Rect(e.NewSize);
        Children.Add(_sharp);
        Children.Add(_blurred);
    }

    internal void SetPose(double amount)
    {
        Amount = amount;
        double blurMix = Math.Min(1, (1 - amount) * 1.5);
        double blurOpacity = amount * blurMix;
        bool showBlur = blurOpacity > .004;
        if (showBlur != UsesCachedBlur)
        {
            _source.Visual = showBlur ? _page : null;
            _page.CacheMode = showBlur ? SharpCache : null;
            _blurred.Visibility = showBlur ? Visibility.Visible : Visibility.Hidden;
        }

        _blurred.Opacity = blurOpacity;
        // Match the page's opacity after compositing the blur over the sharp pass.
        _sharp.Opacity = showBlur ? (amount - blurOpacity) / (1 - blurOpacity) : amount;
    }
}
