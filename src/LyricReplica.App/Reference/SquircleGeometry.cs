using System.Windows;
using System.Windows.Media;

namespace LyricReplica.App.Reference;

internal static class SquircleGeometry
{
    internal static Geometry Create(double width, double height, double radius, double smoothing = 0)
    {
        double budget = Math.Min(width, height) / 2;
        radius = Math.Clamp(radius, 0, Math.Max(0, budget));
        smoothing = Math.Clamp(smoothing, 0, 1);
        if (radius == 0 || smoothing == 0)
            return new RectangleGeometry(new Rect(0, 0, Math.Max(0, width), Math.Max(0, height)), radius, radius);
        double p = (1 + smoothing) * radius, angle = 90 * (1 - smoothing), arc = Math.Sin(Rad(angle / 2)) * radius * Math.Sqrt(2);
        double alpha = (90 - angle) / 2, beta = 45 * smoothing, c = radius * Math.Tan(Rad(alpha / 2)) * Math.Cos(Rad(beta)), d = c * Math.Tan(Rad(beta));
        double b = (p - arc - c - d) / 3, a = 2 * b;
        if (p > budget)
        {
            double available = budget - d - arc - c;
            b = Math.Min(b, available * 5 / 6);
            a = available - b;
            p = budget;
        }
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(width - p, 0), true, true);
            for (int corner = 0; corner < 4; corner++)
            {
                path.LineTo(Map(-p, 0), true, false);
                path.BezierTo(Map(-p + a, 0), Map(-p + a + b, 0), Map(-p + a + b + c, d), true, false);
                double x = -p + a + b + c + arc, y = d + arc;
                path.ArcTo(Map(x, y), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                path.BezierTo(Map(x + d, y + c), Map(x + d, y + b + c), Map(x + d, y + a + b + c), true, false);
                Point Map(double localX, double localY) => corner switch
                {
                    0 => new(width + localX, localY),
                    1 => new(width - localY, height + localX),
                    2 => new(-localX, height - localY),
                    _ => new(localY, -localX)
                };
            }
        }
        geometry.Freeze();
        return geometry;
        static double Rad(double degrees) => degrees * Math.PI / 180;
    }
}
