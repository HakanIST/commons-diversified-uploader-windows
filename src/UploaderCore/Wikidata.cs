using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UploaderCore;

public record WikidataHit(string Qid, string Label, string Description, string? Category = null);

public record TeamInfo(string Qid, string Label, string? Category, Dictionary<string, string> Articles);

public record RosterResult(TeamInfo Team, List<Player> Players, RosterSource? Source);

/// <summary>Takım arama, Vikipedi kadro şablonundan numara → oyuncu tablosu, oyuncuların Commons kategorileri.</summary>
public static partial class Wikidata
{
    private const string Api = "https://www.wikidata.org/w/api.php";
    private static string Wiki(string lang) => $"https://{lang}.wikipedia.org/w/api.php";

    public static async Task<List<WikidataHit>> Search(string query, string lang = "tr", CancellationToken ct = default)
    {
        var d = await Http.GetJson(Api, new() { ["action"] = "wbsearchentities", ["search"] = query, ["language"] = lang, ["uselang"] = lang, ["type"] = "item", ["limit"] = "10", ["format"] = "json" }, ct: ct);
        return (d["search"]?.AsArray() ?? []).Select(r => new WikidataHit(r!["id"]!.GetValue<string>(), r["label"]?.GetValue<string>() ?? r["id"]!.GetValue<string>(), r["description"]?.GetValue<string>() ?? "")).ToList();
    }

    private static async Task<Dictionary<string, JsonNode>> Entities(IEnumerable<string> qids, CancellationToken ct)
    {
        var output = new Dictionary<string, JsonNode>();
        foreach (var part in qids.Distinct().Order().Chunk(50))
        {
            var d = await Http.GetJson(Api, new() { ["action"] = "wbgetentities", ["ids"] = string.Join("|", part), ["props"] = "labels|claims|sitelinks", ["languages"] = "tr|en", ["format"] = "json" }, ct: ct);
            foreach (var (k, v) in d["entities"]?.AsObject() ?? []) if (v != null) output[k] = v;
        }
        return output;
    }

    private static IEnumerable<JsonNode> ClaimValues(JsonNode entity, string property) =>
        (entity["claims"]?[property]?.AsArray() ?? []).Select(c => c?["mainsnak"]?["datavalue"]?["value"]).OfType<JsonNode>();

    private static string? CommonsCategory(JsonNode entity)
    {
        var p373 = ClaimValues(entity, "P373").FirstOrDefault(v => v is JsonValue)?.GetValue<string>();
        if (p373 != null) return p373;
        var title = entity["sitelinks"]?["commonswiki"]?["title"]?.GetValue<string>();
        return title != null && title.StartsWith("Category:") ? title[9..] : null;
    }

    private static string? Label(JsonNode entity) => (entity["labels"]?["tr"]?["value"] ?? entity["labels"]?["en"]?["value"])?.GetValue<string>();

    private static bool IsHuman(JsonNode entity) => ClaimValues(entity, "P31").Any(v => v is JsonObject && v["id"]?.GetValue<string>() == "Q5");

    public static async Task<TeamInfo> Team(string qid, CancellationToken ct = default)
    {
        var e = (await Entities([qid], ct)).GetValueOrDefault(qid);
        if (e == null || e["missing"] != null) throw new ApiException($"Wikidata ögesi bulunamadı: {qid}");
        var articles = new Dictionary<string, string>();
        foreach (var lang in new[] { "tr", "en" })
            if (e["sitelinks"]?[$"{lang}wiki"]?["title"]?.GetValue<string>() is { } t) articles[lang] = t;
        return new TeamInfo(qid, Label(e) ?? qid, CommonsCategory(e), articles);
    }

    internal record RosterEntry(string Number, string Name, string? Title);

    [GeneratedRegex(@"\{\{\s*(?:TBL oyuncu|[Pp]layer\d?|[Bb]asketball player\d?)\s*\|([^{}]*)\}\}")] private static partial Regex PlayerTemplate();
    [GeneratedRegex(@"^\*\s*(\d{1,2})\s+\[\[([^\]|]+)(?:\|[^\]]*)?\]\]", RegexOptions.Multiline)] private static partial Regex ListLine();
    [GeneratedRegex(@"'''?|<[^>]+>")] private static partial Regex Markup();
    [GeneratedRegex(@"\s*\(.*\)$")] private static partial Regex Disambiguator();

    /// <summary>Kadro şablonu vikimetninden numara, ad ve madde başlığı çıkarır.</summary>
    internal static List<RosterEntry> ParseRoster(string wikitext)
    {
        var players = new Dictionary<string, RosterEntry>();
        void Add(string number, string name, string? title)
        {
            number = number.Trim();
            if (number.Length is < 1 or > 2 || !number.All(char.IsAsciiDigit)) return;
            name = Markup().Replace(name, "").Trim();
            title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
            if (players.TryGetValue(number, out var prev) && name.Length <= prev.Name.Length)
            {
                if (prev.Title == null) players[number] = prev with { Title = title };
            }
            else
            {
                // Tam adı taşıyan (şablon satırı) kaydı, kısa etiketli liste satırına tercih et.
                players[number] = new RosterEntry(number, name.Length > 0 ? name : prev?.Name ?? title ?? "", title ?? prev?.Title);
            }
        }
        // {{TBL oyuncu | num = 5 | ilk = Sertaç | son = Şanlı}} ve {{player2 | num | first | last | dab}}
        foreach (Match m in PlayerTemplate().Matches(wikitext))
        {
            var f = new Dictionary<string, string>();
            foreach (var part in m.Groups[1].Value.Split('|'))
            {
                var eq = part.IndexOf('=');
                if (eq > 0) f[part[..eq].Trim().ToLowerInvariant()] = part[(eq + 1)..].Trim();
            }
            string Get(params string[] keys) => keys.Select(k => f.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
            var full = $"{Get("ilk", "first")} {Get("son", "last")}".Trim();
            var name = full.Length > 0 ? full : Get("name", "ad");
            var link = Get("bağlantı", "link");
            var dab = Get("dab");
            Add(Get("num", "no"), name, link.Length > 0 ? link : dab.Length > 0 ? $"{name} ({dab})" : name);
        }
        // "* 31 [[Shavon Shields|Shields]]"
        foreach (Match m in ListLine().Matches(wikitext)) Add(m.Groups[1].Value, m.Groups[2].Value, m.Groups[2].Value);
        return players.Values.OrderBy(p => int.Parse(p.Number)).ThenBy(p => p.Number, StringComparer.Ordinal).ToList();
    }

    private static async Task<string?> RosterTemplate(string lang, string article, CancellationToken ct)
    {
        var d = await Http.GetJson(Wiki(lang), new() { ["action"] = "query", ["prop"] = "templates", ["titles"] = article, ["tlnamespace"] = "10", ["tllimit"] = "max", ["redirects"] = "1", ["format"] = "json", ["formatversion"] = "2" }, ct: ct);
        var names = (d["query"]?["pages"]?[0]?["templates"]?.AsArray() ?? []).Select(t => t!["title"]!.GetValue<string>());
        var needle = lang == "tr" ? "kadro" : "roster";
        var first = Disambiguator().Replace(article, "").Split(' ')[0].ToLowerInvariant();
        return names.FirstOrDefault(n => n.ToLowerInvariant().Contains(needle) && n.ToLowerInvariant().Contains(first));
    }

    private static async Task<string> PageText(string lang, string title, CancellationToken ct)
    {
        var d = await Http.GetJson(Wiki(lang), new() { ["action"] = "query", ["prop"] = "revisions", ["rvprop"] = "content", ["rvslots"] = "main", ["titles"] = title, ["redirects"] = "1", ["format"] = "json", ["formatversion"] = "2" }, ct: ct);
        return d["query"]?["pages"]?[0]?["revisions"]?[0]?["slots"]?["main"]?["content"]?.GetValue<string>() ?? "";
    }

    private static async Task<Dictionary<string, string>> Qids(string lang, IEnumerable<string> titles, CancellationToken ct)
    {
        var map = new Dictionary<string, string>();
        foreach (var part in titles.Distinct().Order().Chunk(50))
        {
            var d = await Http.GetJson(Wiki(lang), new() { ["action"] = "query", ["prop"] = "pageprops", ["ppprop"] = "wikibase_item", ["titles"] = string.Join("|", part), ["redirects"] = "1", ["format"] = "json", ["formatversion"] = "2" }, ct: ct);
            var alias = new Dictionary<string, string>();
            foreach (var n in d["query"]?["normalized"]?.AsArray() ?? []) alias[n!["to"]!.GetValue<string>()] = n["from"]!.GetValue<string>();
            foreach (var r in d["query"]?["redirects"]?.AsArray() ?? [])
            {
                var from = r!["from"]!.GetValue<string>();
                alias[r["to"]!.GetValue<string>()] = alias.GetValueOrDefault(from, from);
            }
            foreach (var p in d["query"]?["pages"]?.AsArray() ?? [])
            {
                var title = p!["title"]!.GetValue<string>();
                if (p["pageprops"]?["wikibase_item"]?.GetValue<string>() is not { } qid) continue;
                map[title] = qid;
                if (alias.TryGetValue(title, out var a)) map[a] = qid;
            }
        }
        return map;
    }

    /// <summary>Bir takımın güncel kadrosu: numara, ad, Wikidata ögesi ve Commons kategorisi.</summary>
    public static async Task<RosterResult> Roster(string teamQid, CancellationToken ct = default)
    {
        var info = await Team(teamQid, ct);
        List<RosterEntry> entries = [];
        RosterSource? source = null;
        foreach (var lang in new[] { "tr", "en" })
        {
            if (!info.Articles.TryGetValue(lang, out var article)) continue;
            var page = await RosterTemplate(lang, article, ct) ?? article;
            var parsed = ParseRoster(await PageText(lang, page, ct));
            if (parsed.Count > entries.Count) { entries = parsed; source = new RosterSource(lang, page); }
            if (entries.Count >= 8) break;
        }
        var players = entries.Select(e => new Player { Number = e.Number, Name = e.Name }).ToList();
        if (source != null && entries.Count > 0)
        {
            var map = await Qids(source.Lang, entries.Select(e => e.Title).OfType<string>(), ct);
            var ents = await Entities(entries.Select(e => e.Title != null ? map.GetValueOrDefault(e.Title) : null).OfType<string>(), ct);
            for (var i = 0; i < entries.Count; i++)
            {
                // Anlam ayrımı ya da başka bir şeye giden bağlantıları ele.
                if (entries[i].Title is not { } title || !map.TryGetValue(title, out var qid) || !ents.TryGetValue(qid, out var e) || !IsHuman(e)) continue;
                players[i].Qid = qid;
                players[i].Category = CommonsCategory(e);
            }
        }
        return new RosterResult(info, players, source);
    }
}
