using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UploaderCore;

/// <summary>0…1 aralığında, görüntü boyutuna oranlı kutu (sol üst köşe başlangıç).</summary>
public record struct NormalizedBox(double X0, double Y0, double X1, double Y1)
{
    [JsonIgnore] public readonly double Width => X1 - X0;
    [JsonIgnore] public readonly double Height => Y1 - Y0;
}

public record NumberDetection(string Number, NormalizedBox Box, int Hits, double HeightRatio, Dictionary<string, double> Colors);

public record TextDetection(string Text, NormalizedBox Box);

public record PhotoAnalysis(int Width, int Height, List<NumberDetection> Numbers, List<TextDetection> Texts);

public class Player
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Qid { get; set; }
    public string? Category { get; set; }
}

public record RosterSource(string Lang, string Page);

public class Team
{
    public string Key { get; set; } = "";
    public string? Qid { get; set; }
    public string Label { get; set; } = "";
    public string Category { get; set; } = "";
    public List<string> Colors { get; set; } = [];
    public List<string> Keywords { get; set; } = [];
    public List<Player> Players { get; set; } = [];
    public RosterSource? RosterSource { get; set; }
}

public class MatchInfo
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public bool CreateCategory { get; set; }
    public List<string> ExtraCategories { get; set; } = [];
    public string Author { get; set; } = "";
    public string License { get; set; } = "cc-by-sa-4.0";
    public string TitleTemplate { get; set; } = "{oyuncular} {numaralar} - {maç} ({sıra})";
    public List<Team> Teams { get; set; } = [new Team { Key = "a" }, new Team { Key = "b" }];
}

public record ManualPlayer(string Team, string Number);

public record UploadResult(string Title, string? Url);

public class PhotoRecord
{
    public bool Include { get; set; } = true;
    public string? Date { get; set; }
    public PhotoAnalysis? Analysis { get; set; }
    public List<ManualPlayer>? Manual { get; set; }
    public string? Title { get; set; }
    public UploadResult? Uploaded { get; set; }
    public string? UploadError { get; set; }
    public string? Error { get; set; }
}

/// <summary>Fotoğraf klasöründe saklanan proje dosyası (commons-yukleme.json); macOS uygulamasıyla aynı biçim.</summary>
public class Project
{
    public const string FileName = "commons-yukleme.json";
    public MatchInfo Match { get; set; } = new();
    public Dictionary<string, PhotoRecord> Photos { get; set; } = [];

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public static Project Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return new Project();
        return JsonSerializer.Deserialize<Project>(File.ReadAllText(path), Json) ?? new Project();
    }

    public void Save(string folder)
    {
        var path = Path.Combine(folder, FileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Klasördeki görüntülerle kayıtları eşitler.</summary>
    public void Sync(IReadOnlyCollection<string> fileNames)
    {
        foreach (var name in fileNames) Photos.TryAdd(name, new PhotoRecord());
        foreach (var name in Photos.Keys.Where(k => !fileNames.Contains(k)).ToList()) Photos.Remove(name);
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".tif", ".tiff" };

    public static List<string> ImageFiles(string folder) =>
        Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>()
            .Where(n => !n.StartsWith('.') && ImageExtensions.Contains(Path.GetExtension(n)))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
}
