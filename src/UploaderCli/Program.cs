using System.Diagnostics;
using System.Text.Json;
using SkiaSharp;
using UploaderCore;

// Geliştirme aracı. Komutlar: fixture <model> <png> <json> | analyze <model> <fotoğraf>... | roster <QID> | match <klasör>
if (args.Length == 0) { Console.WriteLine("komutlar: fixture, analyze, roster, match"); return 2; }
switch (args[0])
{
    case "fixture":
    {
        using var ocr = new FlorenceOcr(args[1]);
        using var bitmap = SKBitmap.Decode(args[2]);
        var rgb = ImageLoader.Rgb(bitmap, new Tile(0, 0, bitmap.Width, bitmap.Height), FlorenceOcr.InputSize, FlorenceOcr.InputSize);
        var expected = JsonDocument.Parse(File.ReadAllText(args[3])).RootElement.GetProperty("tokenIds").EnumerateArray().Select(e => e.GetInt64()).ToArray();
        var sw = Stopwatch.StartNew();
        var tokens = ocr.Generate(rgb);
        Console.WriteLine($"süre: {sw.ElapsedMilliseconds} ms");
        Console.WriteLine(tokens.SequenceEqual(expected) ? $"BELİRTEÇLER AYNI ({tokens.Length})" : $"FARKLI\n beklenen: {string.Join(",", expected)}\n bulunan:  {string.Join(",", tokens)}");
        Console.WriteLine(ocr.Decode(tokens));
        return tokens.SequenceEqual(expected) ? 0 : 1;
    }
    case "analyze":
    {
        using var ocr = new FlorenceOcr(args[1]);
        var analyzer = new ImageAnalyzer(ocr);
        foreach (var path in args.Skip(2))
        {
            var sw = Stopwatch.StartNew();
            var a = analyzer.Analyze(path);
            var nums = a.Numbers.Select(n => $"{n.Number}[{string.Join("+", n.Colors.OrderByDescending(c => c.Value).Take(2).Select(c => $"{c.Key}{(int)(c.Value * 100)}"))} h{n.HeightRatio} x{n.Hits}]");
            Console.WriteLine($"{Path.GetFileName(path)} {sw.ElapsedMilliseconds}ms {string.Join(" ", nums)} | {string.Join(",", a.Texts.Select(t => t.Text))}");
        }
        return 0;
    }
    case "roster":
    {
        var r = await Wikidata.Roster(args[1]);
        Console.WriteLine($"{r.Team.Label} | {r.Team.Category ?? "-"} | {r.Source?.Page ?? "-"}");
        foreach (var p in r.Players) Console.WriteLine($"  {p.Number} {p.Name} {p.Qid ?? "-"} | {p.Category ?? "-"}");
        return 0;
    }
    case "match":
    {
        var project = Project.Load(args[1]);
        foreach (var (name, record) in project.Photos.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (record.Analysis == null) continue;
            var (players, unresolved) = Matcher.Match(record.Analysis, project.Match.Teams);
            Console.WriteLine($"{name} {string.Join(", ", players.Select(p => $"{p.Number} {p.Name}"))}{(unresolved.Count > 0 ? " | çözülemeyen: " + string.Join(",", unresolved.Select(u => u.Number)) : "")}");
        }
        return 0;
    }
    default:
        Console.WriteLine("bilinmeyen komut");
        return 2;
}
