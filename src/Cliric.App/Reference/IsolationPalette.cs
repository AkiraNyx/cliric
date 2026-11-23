using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using ColorThiefDotNet;
using Cliric.Core;

namespace Cliric.App.Reference;

internal static class IsolationPalette
{
    public static Vector3[] Extract(byte[] encoded, IsolationPalettes mode)
    {
        using var stream = new MemoryStream(encoded);
        using var bitmap = new Bitmap(stream);
        var colors = Sample(bitmap);
        if (colors.Count == 0)
            return new Vector3[4];
        if (mode is IsolationPalettes.MedianCut1 or IsolationPalettes.MedianCut2)
        {
            var thief = new ColorThief();
            bool dark = mode == IsolationPalettes.MedianCut1;
            bool dominantDark = thief.GetColor(bitmap, 10, false).IsDark;
            var quantized = thief.GetPalette(bitmap, dark ? 8 : 32, 10, false).Where(color => color.IsDark == dark).OrderByDescending(color => color.Population);
            var palette = (dark ? quantized : quantized.Take(4)).Select(color => new Vector3(color.Color.R, color.Color.G, color.Color.B) / 255).ToList();
            if (dark && palette.Count == 0)
                palette = thief.GetPalette(bitmap, 8, 10, false).Where(color => color.IsDark == dominantDark).OrderByDescending(color => color.Population).Select(color => PaletteMath.Darken(new Vector3(color.Color.R, color.Color.G, color.Color.B), 40) / 255).ToList();
            return palette.Count == 0 ? SourcePalette.Octree(colors).Select(color => color / 255).ToArray() : Enumerable.Range(0, 4).Select(i => palette[i % palette.Count]).ToArray();
        }
        var result = mode switch
        {
            IsolationPalettes.KMeans => SourcePalette.KMeans(colors),
            IsolationPalettes.OctTree => SourcePalette.Octree(colors),
            _ => SourcePalette.Auto(colors)
        };
        return result.Select(color => color / 255).ToArray();
    }
    private static Dictionary<Vector3, int> Sample(Bitmap bitmap)
    {
        var result = new Dictionary<Vector3, int>();
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[bitmap.Width * 4];
            int sampled = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (sampled++ % 10 != 0)
                        continue;
                    int index = x * 4;
                    if (row[index + 3] == 0)
                        continue;
                    var color = new Vector3(row[index + 2], row[index + 1], row[index]);
                    result[color] = result.GetValueOrDefault(color) + 1;
                }
            }
        }
        finally { bitmap.UnlockBits(data); }
        return result;
    }
}
