using System.Windows;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace LyricReplica.App;

internal sealed class PageFadeEffect : ShaderEffect
{
    private static readonly PixelShader Shader = MotionTokens.Freeze(new PixelShader
    {
        UriSource = new Uri("pack://application:,,,/LyricReplica;component/Assets/Shaders/page-fade.ps")
    });
    private static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(PageFadeEffect), 0, SamplingMode.Bilinear);
    private static readonly DependencyProperty KernelProperty = DependencyProperty.Register(
        "Kernel", typeof(Point3D), typeof(PageFadeEffect),
        new UIPropertyMetadata(new Point3D(1, 1, 0), PixelShaderConstantCallback(0)));
    private static readonly DependencyProperty OpacityProperty = DependencyProperty.Register(
        "Opacity", typeof(double), typeof(PageFadeEffect), new UIPropertyMetadata(1d, PixelShaderConstantCallback(1)));

    internal PageFadeEffect()
    {
        DdxUvDdyUvRegisterIndex = 2;
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(KernelProperty);
        UpdateShaderValue(OpacityProperty);
    }

    internal void SetPose(double opacity, double pixelsPerDip)
    {
        double sigma = Math.Max(.2, 3.6 * (1 - opacity) * pixelsPerDip);
        double exponent = -.5 / (sigma * sigma);
        double atOne = Math.Exp(exponent);
        double atTwo = Math.Exp(4 * exponent);
        // One bilinear lookup combines the two adjacent Gaussian taps on each side.
        double pair = atOne + atTwo;
        double normalize = 1 + 2 * pair;
        SetValue(KernelProperty, new Point3D(1 + atTwo / pair, 1 / normalize, pair / normalize));
        SetValue(OpacityProperty, opacity);
    }
}
