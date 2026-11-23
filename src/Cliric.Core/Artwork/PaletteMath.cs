using System.Numerics;

namespace Cliric.Core;

public static class PaletteMath
{
    public static bool IsDark(Vector3 rgb)
    {
        var normalized = rgb / 255;
        float y = .2126f * Linear(normalized.X) + .7152f * Linear(normalized.Y) + .0722f * Linear(normalized.Z);
        float lightness = y > .008856452f ? (float)Math.Pow(y, .3333333432674408) * 116 - 16 : y * 903.2963f;
        return lightness <= 55;
    }
    private static float Linear(float value) => value <= .04045f ? value / 12.92f : (float)Math.Pow((value + .055) / 1.055, 2.4);
    public static Vector3 Darken(Vector3 rgb, float amount)
    {
        float max = Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z)), min = Math.Min(rgb.X, Math.Min(rgb.Y, rgb.Z));
        float value = Math.Max(0, max * 100 / 255 - amount), saturation = max == min ? 0 : (max - min) / max * 100, hue = 0;
        if (max != min)
        {
            hue = max == rgb.X ? 60 * (rgb.Y - rgb.Z) / (max - min) : max == rgb.Y ? 60 * (2 + (rgb.Z - rgb.X) / (max - min)) : 60 * (4 + (rgb.X - rgb.Y) / (max - min));
            if (hue < 0)
                hue += 360;
        }
        if (hue == 360)
            hue = 0;
        int sector = (int)Math.Floor(hue / 60) % 6;
        float fraction = hue / 60 - sector;
        float p = value / 100 * (1 - saturation / 100) * 255, q = value / 100 * (1 - fraction * saturation / 100) * 255, t = value / 100 * (1 - (1 - fraction) * saturation / 100) * 255, v = value * 255 / 100;
        return sector switch
        {
            0 => new(v, t, p),
            1 => new(q, v, p),
            2 => new(p, v, t),
            3 => new(p, q, v),
            4 => new(t, p, v),
            _ => new(v, p, q)
        };
    }
    public static Vector3 ToLab(Vector3 rgb)
    {
        float r = Gamma(rgb.X / 255), g = Gamma(rgb.Y / 255), b = Gamma(rgb.Z / 255);
        float x = r * .4124f + g * .3576f + b * .1805f, y = r * .2126f + g * .7152f + b * .0722f, z = r * .0193f + g * .1192f + b * .9505f;
        return new(116 * LabChannel(y) - 16, 500 * (LabChannel(x / .9505f) - LabChannel(y)), 200 * (LabChannel(y) - LabChannel(z / 1.089f)));
        static float Gamma(float value) => value > .04045 ? (float)Math.Pow((value + .055) / 1.055, 2.2) : (float)(value / 12.92);
        static float LabChannel(float value) => value > .008856 ? (float)Math.Pow(value, 1d / 3) : 7.787f * value + .13793103f;
    }
    public static double Variance(IReadOnlyList<Vector3> palette)
    {
        if (palette.Count == 0)
            return 0;
        var lab = palette.Select(ToLab).ToList();
        Vector3 mean = Vector3.Zero;
        foreach (var color in lab)
            mean += color;
        mean /= lab.Count;
        return lab.Sum(color => Vector3.DistanceSquared(color, mean)) / lab.Count;
    }
}
