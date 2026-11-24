using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SkiaSharp;

namespace UploaderCore;

public static class JerseyColor
{
    public static readonly string[] Names = ["siyah", "beyaz", "gri", "kırmızı", "turuncu", "sarı", "yeşil", "turkuaz", "mavi", "lacivert", "mor", "pembe", "bordo"];

    public static string Name(byte r, byte g, byte b)
    {
        double rf = r, gf = g, bf = b;
        double mx = Math.Max(rf, Math.Max(gf, bf)), mn = Math.Min(rf, Math.Min(gf, bf));
        double v = mx / 255, s = mx == 0 ? 0 : (mx - mn) / mx;
        if (v < 0.2) return "siyah";
        if (s < 0.18) return v > 0.75 ? "beyaz" : v < 0.35 ? "siyah" : "gri";
        var d = mx - mn;
        double h;
        if (mx == rf) h = (gf - bf) / d % 6; else if (mx == gf) h = (bf - rf) / d + 2; else h = (rf - gf) / d + 4;
        h = (h * 60 + 360) % 360;
        if (h < 15 || h >= 345) return v < 0.5 ? "bordo" : "kırmızı";
        if (h < 40) return v < 0.45 ? "bordo" : "turuncu";
        if (h < 70) return "sarı";
        if (h < 160) return "yeşil";
        if (h < 195) return "turkuaz";
        if (h < 255) return v < 0.45 ? "lacivert" : "mavi";
        if (h < 290) return "mor";
        return v < 0.5 ? "bordo" : "pembe";
    }
}

public record struct Tile(int X, int Y, int Width, int Height);

public static class ImageLoader
{
    /// <summary>EXIF yönü uygulanmış, uzun kenarı en çok <paramref name="maxPixel"/> olan görüntü.</summary>
    public static SKBitmap? Load(string path, int maxPixel)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null) return null;
        // JPEG için çözücünün kendi küçültmesini kullan: tam çözünürlüğü belleğe almaz.
        var info = codec.Info;
        var scale = Math.Min(1f, (float)maxPixel / Math.Max(info.Width, info.Height));
        var scaled = codec.GetScaledDimensions(Math.Max(scale, 0.125f));
        var decodeInfo = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var decoded = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) { decoded.Dispose(); return null; }
        var oriented = Orient(decoded, codec.EncodedOrigin);
        var longSide = Math.Max(oriented.Width, oriented.Height);
        if (longSide <= maxPixel) return oriented;
        var f = (double)maxPixel / longSide;
        var target = new SKImageInfo((int)Math.Round(oriented.Width * f), (int)Math.Round(oriented.Height * f), SKColorType.Rgba8888, SKAlphaType.Opaque);
        var resized = oriented.Resize(target, new SKSamplingOptions(SKCubicResampler.CatmullRom));
        oriented.Dispose();
        return resized;
    }

    private static SKBitmap Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return bitmap;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var output = new SKBitmap(new SKImageInfo(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(output))
        {
            float w = bitmap.Width, h = bitmap.Height;
            // x' = a·x + b·y + tx, y' = c·x + d·y + ty
            var m = origin switch
            {
                SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
                SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
                SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
                SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
                SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
                _ => SKMatrix.Identity,
            };
            canvas.SetMatrix(m);
            canvas.DrawBitmap(bitmap, 0, 0);
        }
        bitmap.Dispose();
        return output;
    }

    /// <summary>EXIF çekim zamanı, "YYYY-MM-DD HH:MM:SS" biçiminde.</summary>
    public static string? CaptureDate(string path)
    {
        try
        {
            var sub = ImageMetadataReader.ReadMetadata(path).OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var raw = sub?.GetString(ExifDirectoryBase.TagDateTimeOriginal) ?? sub?.GetString(ExifDirectoryBase.TagDateTimeDigitized);
            if (raw == null || raw.Length < 19) return null;
            var parts = raw.Split(' ');
            return parts.Length == 2 ? parts[0].Replace(':', '-') + " " + parts[1] : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Bitmap'in bir bölümünü hedef boyuta ölçekleyip RGB bayt dizisi olarak döndürür.</summary>
    public static byte[] Rgb(SKBitmap source, Tile crop, int width, int height)
    {
        using var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(target))
        using (var image = SKImage.FromBitmap(source))
        {
            canvas.DrawImage(image, new SKRect(crop.X, crop.Y, crop.X + crop.Width, crop.Y + crop.Height), new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.CatmullRom));
        }
        var rgba = target.GetPixelSpan();
        var rgb = new byte[width * height * 3];
        for (int i = 0, n = width * height; i < n; i++)
        {
            rgb[i * 3] = rgba[i * 4]; rgb[i * 3 + 1] = rgba[i * 4 + 1]; rgb[i * 3 + 2] = rgba[i * 4 + 2];
        }
        return rgb;
    }
}

public sealed class ImageAnalyzer(FlorenceOcr ocr)
{
    public const int WorkLongSide = 3072;

    /// <summary>Tam kare + örtüşen ızgara parçaları.</summary>
    public static List<Tile> Tiles(int width, int height, int cols, int rows, double overlap = 0.25)
    {
        var tiles = new List<Tile> { new(0, 0, width, height) };
        if (cols * rows <= 1) return tiles;
        var tw = (int)Math.Round(width / (cols - (cols - 1) * overlap), MidpointRounding.AwayFromZero);
        var th = (int)Math.Round(height / (rows - (rows - 1) * overlap), MidpointRounding.AwayFromZero);
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var x = cols == 1 ? 0 : (int)Math.Round((double)c * (width - tw) / (cols - 1), MidpointRounding.AwayFromZero);
                var y = rows == 1 ? 0 : (int)Math.Round((double)r * (height - th) / (rows - 1), MidpointRounding.AwayFromZero);
                tiles.Add(new Tile(x, y, tw, th));
            }
        return tiles;
    }

    private static Dictionary<string, double> ColorsAround(TextRegion r, ReadOnlySpan<byte> rgba, int width, int height)
    {
        int x0 = Math.Max(0, (int)Math.Round(r.X - r.Width * 0.6)), x1 = Math.Min(width, (int)Math.Round(r.X + r.Width * 1.6));
        int y0 = Math.Max(0, (int)Math.Round(r.Y - r.Height * 0.5)), y1 = Math.Min(height, (int)Math.Round(r.Y + r.Height * 1.5));
        var hist = new Dictionary<string, int>();
        if (x1 <= x0 || y1 <= y0) return [];
        var step = Math.Max(1, (int)Math.Round(Math.Min(x1 - x0, y1 - y0) / 40.0));
        var n = 0;
        for (var y = y0; y < y1; y += step)
            for (var x = x0; x < x1; x += step)
            {
                var i = (y * width + x) * 4;
                var name = JerseyColor.Name(rgba[i], rgba[i + 1], rgba[i + 2]);
                hist[name] = hist.GetValueOrDefault(name) + 1;
                n++;
            }
        return hist.ToDictionary(p => p.Key, p => Math.Round((double)p.Value / n, 3));
    }

    private static double Iou(TextRegion a, TextRegion b)
    {
        var ix = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
        var iy = Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
        var inter = ix * iy;
        var union = a.Width * a.Height + b.Width * b.Height - inter;
        return union > 0 ? inter / union : 0;
    }

    public PhotoAnalysis Analyze(string path, int cols = 3, int rows = 2)
    {
        using var image = ImageLoader.Load(path, WorkLongSide) ?? throw new InvalidOperationException($"Görüntü açılamadı: {Path.GetFileName(path)}");
        int w = image.Width, h = image.Height;
        if (w < h) (cols, rows) = (rows, cols);
        var merged = new List<(TextRegion Region, int Hits)>();
        foreach (var tile in Tiles(w, h, cols, rows))
        {
            var input = ImageLoader.Rgb(image, tile, FlorenceOcr.InputSize, FlorenceOcr.InputSize);
            foreach (var found in ocr.Recognize(input, tile.Width, tile.Height))
            {
                var region = found with { X = found.X + tile.X, Y = found.Y + tile.Y };
                var i = merged.FindIndex(m => m.Region.Text == region.Text && Iou(m.Region, region) > 0.3);
                if (i >= 0) merged[i] = (merged[i].Region, merged[i].Hits + 1); else merged.Add((region, 1));
            }
        }
        var numbers = new List<NumberDetection>();
        var texts = new List<TextDetection>();
        var pixels = image.GetPixelSpan();
        foreach (var (r, hits) in merged)
        {
            var box = new NormalizedBox(r.X / w, r.Y / h, (r.X + r.Width) / w, (r.Y + r.Height) / h);
            if (r.Text.Length is 1 or 2 && r.Text.All(char.IsAsciiDigit))
                numbers.Add(new NumberDetection(r.Text, box, hits, Math.Round(box.Height, 4), ColorsAround(r, pixels, w, h)));
            else if (r.Text.Length >= 3)
                texts.Add(new TextDetection(r.Text, box));
        }
        return new PhotoAnalysis(w, h, numbers, texts);
    }
}
