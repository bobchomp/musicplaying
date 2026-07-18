using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicDisplay.Services;

/// <summary>
/// Picks a single accent color out of an album cover so the display background can shift to
/// match whatever is playing. Downsamples the artwork, buckets pixels by hue, and returns the
/// average color of the most prominent vivid hue (ignoring near-black/near-white/greyscale
/// pixels so covers with plain borders don't just produce grey), darkened so white text stays
/// readable on top of it.
/// </summary>
public static class ColorExtractor
{
    private const int SampleSize = 24;
    private const int HueBuckets = 24;

    public static Color GetAccentColor(BitmapSource? source, Color fallback)
    {
        if (source == null || source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return fallback;
        }

        try
        {
            var converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            double scaleX = (double)SampleSize / converted.PixelWidth;
            double scaleY = (double)SampleSize / converted.PixelHeight;
            var scaled = new TransformedBitmap(converted, new ScaleTransform(scaleX, scaleY));

            int w = scaled.PixelWidth;
            int h = scaled.PixelHeight;
            if (w <= 0 || h <= 0)
            {
                return fallback;
            }

            int stride = w * 4;
            var pixels = new byte[stride * h];
            scaled.CopyPixels(pixels, stride, 0);

            var weight = new double[HueBuckets];
            var sumR = new double[HueBuckets];
            var sumG = new double[HueBuckets];
            var sumB = new double[HueBuckets];

            for (int i = 0; i + 3 < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];
                if (a < 16)
                {
                    continue;
                }

                RgbToHsv(r, g, b, out double hue, out double sat, out double val);

                if (val < 0.08 || val > 0.97 || sat < 0.12)
                {
                    continue;
                }

                int bucket = Math.Clamp((int)(hue / 360.0 * HueBuckets), 0, HueBuckets - 1);
                double pixelWeight = sat * val;
                weight[bucket] += pixelWeight;
                sumR[bucket] += r * pixelWeight;
                sumG[bucket] += g * pixelWeight;
                sumB[bucket] += b * pixelWeight;
            }

            int best = -1;
            double bestWeight = 0;
            for (int i = 0; i < HueBuckets; i++)
            {
                if (weight[i] > bestWeight)
                {
                    bestWeight = weight[i];
                    best = i;
                }
            }

            if (best < 0)
            {
                return fallback;
            }

            byte avgR = (byte)Math.Clamp(sumR[best] / weight[best], 0, 255);
            byte avgG = (byte)Math.Clamp(sumG[best] / weight[best], 0, 255);
            byte avgB = (byte)Math.Clamp(sumB[best] / weight[best], 0, 255);

            return DarkenForBackground(Color.FromRgb(avgR, avgG, avgB));
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static Color DarkenForBackground(Color color)
    {
        RgbToHsv(color.R, color.G, color.B, out double h, out double s, out double v);
        s = Math.Min(1.0, s * 1.05 + 0.05);
        v = Math.Clamp(v, 0.16, 0.40);
        HsvToRgb(h, s, v, out byte r, out byte g, out byte b);
        return Color.FromRgb(r, g, b);
    }

    private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        double delta = max - min;

        h = 0;
        if (delta > 0.00001)
        {
            if (max == rf)
            {
                h = 60 * (((gf - bf) / delta) % 6);
            }
            else if (max == gf)
            {
                h = 60 * (((bf - rf) / delta) + 2);
            }
            else
            {
                h = 60 * (((rf - gf) / delta) + 4);
            }
        }

        if (h < 0)
        {
            h += 360;
        }

        s = max <= 0 ? 0 : delta / max;
        v = max;
    }

    private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;

        double rf, gf, bf;
        if (h < 60) (rf, gf, bf) = (c, x, 0);
        else if (h < 120) (rf, gf, bf) = (x, c, 0);
        else if (h < 180) (rf, gf, bf) = (0, c, x);
        else if (h < 240) (rf, gf, bf) = (0, x, c);
        else if (h < 300) (rf, gf, bf) = (x, 0, c);
        else (rf, gf, bf) = (c, 0, x);

        r = (byte)Math.Clamp((rf + m) * 255, 0, 255);
        g = (byte)Math.Clamp((gf + m) * 255, 0, 255);
        b = (byte)Math.Clamp((bf + m) * 255, 0, 255);
    }
}
