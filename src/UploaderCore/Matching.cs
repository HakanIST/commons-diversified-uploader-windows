using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UploaderCore;

public class MatchedPlayer
{
    public string Team { get; set; } = "";
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Qid { get; set; }
    public string? Category { get; set; }
    public NormalizedBox? Box { get; set; }
    public double? Confidence { get; set; }
}

public record UnresolvedNumber(string Number, NormalizedBox Box, string? Team);

public static partial class Matcher
{
    /// <summary>Kare yüksekliğine oranla en küçük numara boyu.</summary>
    public const double MinHeight = 0.018;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Normalize(string s)
    {
        var lowered = s.ToLower(Turkish).Replace('ı', 'i').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in lowered)
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
        return sb.ToString();
    }

    internal static int EditDistance(string a, string b)
    {
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        var cur = new int[b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    public static bool KeywordHit(string text, IEnumerable<string> keywords)
    {
        var t = Normalize(text);
        if (t.Length < 4) return false;
        return keywords.Any(k =>
        {
            var n = Normalize(k);
            if (n.Length < 4) return false;
            return t.Contains(n) || (n.Contains(t) && t.Length >= n.Length - 2) || EditDistance(t, n) <= Math.Max(1, n.Length / 3);
        });
    }

    /// <summary>Numaranın hemen üstünde/altında takımın adı ya da sponsor yazısı var mı?</summary>
    private static bool JerseyTextNear(NumberDetection det, List<TextDetection> texts, Team team)
    {
        var b = det.Box;
        var h = b.Height;
        return texts.Any(t =>
        {
            var overlapX = Math.Min(t.Box.X1, b.X1 + h) - Math.Max(t.Box.X0, b.X0 - h) > 0;
            var dy = Math.Min(Math.Abs(t.Box.Y1 - b.Y0), Math.Abs(t.Box.Y0 - b.Y1));
            return overlapX && dy < h * 2.5 && KeywordHit(t.Text, team.Keywords);
        });
    }

    private static bool SameNumber(string a, string b) => a == b ||
        (int.TryParse(a, out var x) && int.TryParse(b, out var y) && x == y && (a.Length > 1 && a[0] == '0') == (b.Length > 1 && b[0] == '0'));

    public static (List<MatchedPlayer> Players, List<UnresolvedNumber> Unresolved) Match(PhotoAnalysis analysis, List<Team> teams)
    {
        var players = new List<MatchedPlayer>();
        var unresolved = new List<UnresolvedNumber>();
        foreach (var det in analysis.Numbers.Where(d => d.HeightRatio >= MinHeight))
        {
            // Yalnızca tek parçada görülen bir rakam, yanında forma yazısı yoksa güvenilmez (ör. alevde uydurma "1").
            if (det.Hits < 2 && !teams.Any(t => JerseyTextNear(det, analysis.Texts, t))) continue;
            var s = teams.Select(t => t.Colors.Sum(c => det.Colors.GetValueOrDefault(c)) + (JerseyTextNear(det, analysis.Texts, t) ? 0.6 : 0)).ToList();
            var order = Enumerable.Range(0, s.Count).OrderByDescending(i => s[i]).ToList();
            var inRoster = teams.Select(t => t.Players.FirstOrDefault(p => SameNumber(p.Number, det.Number))).ToList();
            int? ti = null;
            if (order.Count > 0 && s[order[0]] >= 0.4 && (order.Count < 2 || s[order[0]] - s[order[1]] >= 0.15)) ti = order[0];
            // Numara yalnızca bir takımın kadrosunda varsa ve renk karşı çıkmıyorsa onu seç.
            if (ti == null || inRoster[ti.Value] == null)
            {
                var only = Enumerable.Range(0, inRoster.Count).Where(i => inRoster[i] != null).ToList();
                if (only.Count == 1 && s[only[0]] >= 0.25 && (ti == null || s[only[0]] >= s[ti.Value] - 0.1)) ti = only[0];
            }
            var player = ti is { } idx ? inRoster[idx] : null;
            if (player == null || ti == null)
            {
                unresolved.Add(new UnresolvedNumber(det.Number, det.Box, ti is { } k ? teams[k].Key : null));
                continue;
            }
            var team = teams[ti.Value];
            var confidence = Math.Round(Math.Min(1, s[ti.Value]) * (det.Hits > 1 ? 1 : 0.85), 2);
            var existing = players.FirstOrDefault(p => p.Team == team.Key && p.Number == player.Number);
            if (existing != null) { existing.Confidence = Math.Max(existing.Confidence ?? 0, confidence); continue; }
            players.Add(new MatchedPlayer { Team = team.Key, Number = player.Number, Name = player.Name, Qid = player.Qid, Category = player.Category, Box = det.Box, Confidence = confidence });
        }
        return (players.OrderByDescending(p => p.Box?.Height ?? 0).ToList(), unresolved);
    }
}

public static partial class PageBuilder
{
    [GeneratedRegex(@"[#<>\[\]|{}:/\\]")] private static partial Regex BadTitleChars();
    [GeneratedRegex(@"\(\s*\)")] private static partial Regex EmptyParens();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"^[\s\-–,]+|[\s\-–,]+$")] private static partial Regex Edges();

    /// <summary>Şablon alanları: {oyuncular} {numaralar} {maç} {sıra} ya da {players} {numbers} {match} {n}</summary>
    public static string Title(string template, IReadOnlyList<MatchedPlayer> players, string matchTitle, int seq, string ext)
    {
        var shown = players.Take(3).ToList();
        var t = template
            .Replace("{oyuncular}", string.Join(", ", shown.Select(p => p.Name)))
            .Replace("{numaralar}", string.Join(", ", shown.Select(p => p.Number)))
            .Replace("{maç}", matchTitle)
            .Replace("{sıra}", seq.ToString(CultureInfo.InvariantCulture))
            .Replace("{players}", string.Join(", ", shown.Select(p => p.Name)))
            .Replace("{numbers}", string.Join(", ", shown.Select(p => p.Number)))
            .Replace("{match}", matchTitle)
            .Replace("{n}", seq.ToString(CultureInfo.InvariantCulture));
        t = Edges().Replace(Spaces().Replace(EmptyParens().Replace(BadTitleChars().Replace(t, " "), ""), " "), "");
        return $"{t}.{ext}";
    }

    public static string Describe(IReadOnlyList<MatchedPlayer> players, string matchTitle, string lang)
    {
        var names = players.Select(p => p.Name).ToList();
        if (names.Count == 0) return matchTitle;
        var list = names.Count > 1 ? $"{string.Join(", ", names.Take(names.Count - 1))} {(lang == "tr" ? "ve" : "and")} {names[^1]}" : names[0];
        return matchTitle.Length == 0 ? list : $"{list}, {matchTitle}";
    }

    public static string Wikitext(string descriptionTr, string descriptionEn, string? date, string author, string license, IEnumerable<string> categories)
    {
        var desc = new List<string>();
        if (descriptionTr.Length > 0) desc.Add($"{{{{tr|1={descriptionTr}}}}}");
        if (descriptionEn.Length > 0) desc.Add($"{{{{en|1={descriptionEn}}}}}");
        var cats = string.Join("\n", categories.Where(c => c.Length > 0).Distinct().Select(c => $"[[Category:{c}]]"));
        return "=={{int:filedesc}}==\n{{Information\n" +
               $"|description={string.Join("\n", desc)}\n|date={date ?? ""}\n|source={{{{own}}}}\n|author={author}\n}}}}\n\n" +
               $"=={{{{int:license-header}}}}==\n{{{{self|{license}}}}}\n\n{(cats.Length == 0 ? "{{subst:unc}}" : cats)}\n";
    }
}
