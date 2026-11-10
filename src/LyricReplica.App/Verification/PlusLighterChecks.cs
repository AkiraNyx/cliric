using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricReplica.App.Reference;
using LyricReplica.Core;

namespace LyricReplica.App;

internal static class PlusLighterChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("plus-lighter-original.json")!;
        using var fixture = JsonDocument.Parse(stream);
        bool changedInput = false;
        check(fixture.RootElement.GetProperty("originalShaderAssetExecuted").GetBoolean(), "blend fixture executes the original compiled shader assets");
        foreach (var item in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            int[] input = item.GetProperty("inputArgb").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            int[] expected = item.GetProperty("outputBgra").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var effect = ReferencePlusLighterEffect.ForPreset((PlusLighterEffectPresetTypes)item.GetProperty("preset").GetInt32());
            var border = new Border { Width = 8, Height = 8, Background = new SolidColorBrush(Color.FromArgb((byte)input[0], (byte)input[1], (byte)input[2], (byte)input[3])), Effect = effect };
            border.Measure(new Size(8, 8));
            border.Arrange(new Rect(0, 0, 8, 8));
            border.UpdateLayout();
            var bitmap = new RenderTargetBitmap(8, 8, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(border);
            var pixels = new byte[256];
            bitmap.CopyPixels(pixels, 32, 0);
            int center = (4 * 8 + 4) * 4;
            check(Enumerable.Range(0, 4).All(i => Math.Abs(pixels[center + i] - expected[i]) <= 1), "source blend shader pixel and register contract");
            changedInput |= Math.Abs(pixels[center] - input[3] * input[0] / 255d) > 2;
        }
        check(changedInput, "blend verification detects an enabled effect rather than an unmodified input");
    }
}
