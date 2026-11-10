using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricReplica.App.Reference;
using LyricReplica.Core;

namespace LyricReplica.App;

internal static class IsolationGpuChecks
{
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var results = new List<object>();
        foreach (bool software in new[] { true, false })
        {
            using var graphics = new IsolationGraphics(shared: false, software: software);
            Vector3[] palette = [new(.05f, .12f, .4f), new(.3f, .1f, .45f), new(.08f, .35f, .25f), new(.5f, .16f, .06f)];
            graphics.Resize(64, 48);
            graphics.Draw(0, palette, 20f / 255);
            byte[] first = graphics.ReadPixels();
            if (first.Length != 64 * 48 * 4 || !Enumerable.Range(0, 64 * 48).Any(i => first[i * 4] > 5 || first[i * 4 + 1] > 5 || first[i * 4 + 2] > 5))
                throw new InvalidDataException("Isolation shader did not produce colored output.");
            graphics.Draw(1.25f, palette, 20f / 255);
            byte[] later = graphics.ReadPixels();
            if (first.SequenceEqual(later))
                throw new InvalidDataException("Isolation shader animation did not advance.");
            graphics.Resize(96, 64);
            graphics.Draw(1.25f, palette, 0);
            byte[] resized = graphics.ReadPixels();
            if (resized.Length != 96 * 64 * 4)
                throw new InvalidDataException("Isolation target did not resize.");
            var bitmap = BitmapSource.Create(96, 64, 96, 96, PixelFormats.Bgra32, null, resized, 96 * 4);
            File.WriteAllBytes(Path.Combine(folder, software ? "warp.png" : "hardware.png"), Artwork.Encode(bitmap));
            results.Add(new
            {
                software,
                passed = true,
                firstSha = Convert.ToHexString(SHA256.HashData(first)),
                laterSha = Convert.ToHexString(SHA256.HashData(later)),
                resizedSha = Convert.ToHexString(SHA256.HashData(resized))
            });
        }
        using (var shared = new IsolationGraphics(shared: true))
        using (var bridge = new IsolationSurfaceBridge())
        {
            shared.Resize(64, 48);
            bridge.Attach(shared.Texture, 96, 96);
            Vector3[] colors = [new(.1f, .2f, .4f), new(.4f, .1f, .3f), new(.1f, .4f, .2f), new(.6f, .3f, .1f)];
            bridge.Draw(() => shared.Draw(.5f, colors, 0));
            if (bridge.Source.PixelWidth != 64 || bridge.Source.PixelHeight != 48)
                throw new InvalidDataException("Isolation D3DImage bridge has an invalid surface size.");
            bridge.Attach(shared.Texture, 120, 120);
            if (Math.Abs(bridge.Source.Width - 64 * 96d / 120) > .001 || Math.Abs(bridge.Source.Height - 48 * 96d / 120) > .001)
                throw new InvalidDataException("Isolation bridge did not update DPI on an unchanged texture.");
            shared.Resize(96, 64);
            bridge.Attach(shared.Texture, 120, 120);
            bridge.Draw(() => shared.Draw(1, colors, 0));
            if (bridge.Source.PixelWidth != 96 || bridge.Source.PixelHeight != 64)
                throw new InvalidDataException("Isolation shared bridge did not rebind after resize.");
        }
        File.WriteAllText(Path.Combine(folder, "isolation-gpu.json"), JsonSerializer.Serialize(new
        {
            passed = true,
            verifiedAtUtc = DateTimeOffset.UtcNow,
            windowShown = false,
            sharedD3DImageBridgeChecked = true,
            scope = "Own Direct2D renderer using original DXBC and verified constants; WARP/hardware color, time, size and D3D9/D3DImage binding checks; not original full-page comparison",
            results
        }, LyricsFile.JsonOptions));
    }
}
