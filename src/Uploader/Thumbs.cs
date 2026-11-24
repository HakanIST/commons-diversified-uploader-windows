using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using UploaderCore;

namespace Uploader;

/// <summary>Küçük resimleri ve kutulu önizlemeleri arka planda üretip önbellek klasöründe tutar.</summary>
internal static class Thumbs
{
    private static readonly string CacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommonsDiversifiedUploader", "cache");
    private static readonly SemaphoreSlim Gate = new(2);

    private static string Key(string path, string suffix)
    {
        var info = new FileInfo(path);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{suffix}"));
        return Path.Combine(CacheDir, Convert.ToHexString(hash, 0, 12) + ".jpg");
    }

    private static void Write(SKBitmap bitmap, string target)
    {
        Directory.CreateDirectory(CacheDir);
        var temp = target + "." + Environment.CurrentManagedThreadId + ".tmp";
        using (var stream = File.Create(temp)) bitmap.Encode(stream, SKEncodedImageFormat.Jpeg, 85);
        File.Move(temp, target, overwrite: true);
    }

    /// <summary>Uzun kenarı <paramref name="size"/> olan küçük resmin dosya yolu; açılamazsa null.</summary>
    public static async Task<string?> Thumbnail(string path, int size = 420)
    {
        var target = Key(path, $"t{size}");
        if (File.Exists(target)) return target;
        await Gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                if (File.Exists(target)) return target;
                using var bitmap = ImageLoader.Load(path, size);
                if (bitmap == null) return null;
                Write(bitmap, target);
                return target;
            });
        }
        catch (Exception) { return null; }
        finally { Gate.Release(); }
    }

    /// <summary>Bulunan oyuncuların (yeşil) ve çözülemeyen numaraların (turuncu) kutularıyla önizleme.</summary>
    public static async Task<string?> Preview(string path, PhotoRow row, int size = 1400)
    {
        var signature = string.Join(";", row.Players.Select(p => $"{p.Number}{p.Name}{p.Box}")) + "|" + string.Join(";", row.Unresolved.Select(u => $"{u.Number}{u.Box}"));
        var target = Key(path, $"p{size}|{signature}");
        if (File.Exists(target)) return target;
        try
        {
            return await Task.Run(() =>
            {
                using var bitmap = ImageLoader.Load(path, size);
                if (bitmap == null) return null;
                using (var canvas = new SKCanvas(bitmap))
                {
                    float w = bitmap.Width, h = bitmap.Height;
                    using var font = new SKFont(SKTypeface.Default, Math.Max(14, w / 70));
                    void Box(NormalizedBox b, string label, SKColor color)
                    {
                        var rect = new SKRect((float)b.X0 * w, (float)b.Y0 * h, (float)b.X1 * w, (float)b.Y1 * h);
                        using var stroke = new SKPaint { Color = color, IsStroke = true, StrokeWidth = Math.Max(2, w / 500), IsAntialias = true };
                        canvas.DrawRoundRect(rect, 4, 4, stroke);
                        var width = font.MeasureText(label) + 12;
                        var tag = new SKRect(rect.Left, rect.Top - font.Size - 8, rect.Left + width, rect.Top);
                        if (tag.Top < 0) tag.Offset(0, -tag.Top);
                        using var fill = new SKPaint { Color = color, IsAntialias = true };
                        canvas.DrawRoundRect(tag, 4, 4, fill);
                        using var text = new SKPaint { Color = SKColors.Black, IsAntialias = true };
                        canvas.DrawText(label, tag.Left + 6, tag.Bottom - 6, font, text);
                    }
                    foreach (var p in row.Players) if (p.Box is { } b) Box(b, $"{p.Number} {p.Name}", new SKColor(0x57, 0xe3, 0x89));
                    foreach (var u in row.Unresolved) Box(u.Box, $"{u.Number} ?", new SKColor(0xff, 0xa3, 0x48));
                }
                Write(bitmap, target);
                return target;
            });
        }
        catch (Exception) { return null; }
    }
}
