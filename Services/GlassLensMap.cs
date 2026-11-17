using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIslandWin.Services;

// Snell model adapted from liquid_glass_easy's computeRefractedPosition.
// Copyright (c) 2025 Ahmed Gamil, MIT. See licenses/liquid_glass_easy.
// The expensive shape work runs once per geometry, never per captured frame.
internal static class GlassLensMap
{
    internal const double MaxDisplacement = 12;
    internal readonly record struct Sample(double X, double Y, double Rim);

    internal static Sample Evaluate(double x, double y, double width, double height, double radius,
        double index = 1.08, double lightX = -.6, double lightY = -.8)
    {
        radius = Math.Clamp(radius, .5, Math.Min(width, height) / 2);
        double px = x - width / 2, py = y - height / 2;
        double qx = Math.Abs(px) - width / 2 + radius, qy = Math.Abs(py) - height / 2 + radius;
        double vx = Math.Max(qx, 0), vy = Math.Max(qy, 0), length = Math.Sqrt(vx * vx + vy * vy);
        double distance = length + Math.Min(Math.Max(qx, qy), 0) - radius;
        double nx, ny;
        if (length > .0001) { nx = Math.Sign(px) * vx / length; ny = Math.Sign(py) * vy / length; }
        else if (qx > qy) { nx = Math.Sign(px); ny = 0; }
        else { nx = 0; ny = Math.Sign(py); }
        double band = Math.Min(12, Math.Max(3, radius * .65));
        double h = Math.Clamp(-distance / band, 0, 1);
        double bulge = Math.Sqrt(Math.Max(0, 1 - h * h));
        double eta = 1 / Math.Max(1, index);
        double k = Math.Max(0, 1 - eta * eta * (1 - h * h));
        double bend = (eta * h - Math.Sqrt(k)) * bulge * 22;
        double ramp = Math.Pow(Math.Clamp(1 + distance / 4, 0, 1), 5);
        double facing = nx * lightX + ny * lightY;
        double rim = ramp * (.12 + .88 * facing * facing * (facing >= 0 ? 1 : .45));
        return new(Math.Clamp(nx * bend, -MaxDisplacement, MaxDisplacement),
            Math.Clamp(ny * bend, -MaxDisplacement, MaxDisplacement), rim);
    }

    internal static BitmapSource Create(double width, double height, double radius)
    {
        // Bound work during a resize; hardware linearly interpolates the map.
        double scale = Math.Min(1, 768 / Math.Max(width, height));
        int w = Math.Max(2, (int)Math.Ceiling(width * scale)), h = Math.Max(2, (int)Math.Ceiling(height * scale));
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var sample = Evaluate((x + .5) * width / w, (y + .5) * height / h, width, height, radius);
                int at = (y * w + x) * 4;
                pixels[at] = (byte)Math.Round(Math.Clamp(sample.Rim, 0, 1) * 255);
                pixels[at + 1] = (byte)Math.Round(128 + sample.Y / MaxDisplacement * 127);
                pixels[at + 2] = (byte)Math.Round(128 + sample.X / MaxDisplacement * 127);
                pixels[at + 3] = 255;
            }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
