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
    private static readonly DependencyProperty PoseProperty = DependencyProperty.Register(
        "Pose", typeof(Point3D), typeof(PageFadeEffect),
        new UIPropertyMetadata(new Point3D(0, 0, 1), PixelShaderConstantCallback(0)));

    internal PageFadeEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(PoseProperty);
    }

    internal void SetPose(Size size, double opacity)
    {
        double radius = 5 * (1 - opacity);
        SetValue(PoseProperty, new Point3D(radius / Math.Max(1, size.Width), radius / Math.Max(1, size.Height), opacity));
    }
}
