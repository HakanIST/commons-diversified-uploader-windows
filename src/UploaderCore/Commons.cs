using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UploaderCore;

public abstract record CommonsAuth
{
    public sealed record BotPassword(string Username, string Password) : CommonsAuth;
    public sealed record OAuth2(string Token) : CommonsAuth;
}

/// <summary>Yükleme sonucu: başarı ya da yayımlamayı engelleyen neden (aynı ad, aynı içerik).</summary>
public record UploadOutcome(bool Success, string? Title = null, long? PageId = null, string? Url = null, string? BlockedReason = null);

/// <summary>Commons API istemcisi: oturum, kategori/dosya adı denetimi, parçalı yükleme, "depicts" verisi.</summary>
public sealed class CommonsClient(CommonsAuth? auth, string api = "https://commons.wikimedia.org/w/api.php") : IDisposable
{
    public const int ChunkSize = 4 * 1024 * 1024;
    // Oturum çerezleri yalnızca bellekte tutulur.
    private readonly HttpClient _client = Http.CreateClient(cookies: true);
    private string? _csrf;
    public string? Username { get; private set; }

    private HttpRequestMessage Authorize(HttpRequestMessage request)
    {
        if (auth is CommonsAuth.OAuth2 o) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.Token);
        return request;
    }

    private static Dictionary<string, string> WithFormat(Dictionary<string, string> p) => new(p) { ["format"] = "json", ["formatversion"] = "2" };

    private Task<JsonNode> Get(Dictionary<string, string> p, CancellationToken ct) =>
        Http.SendJson(_client, () => Authorize(new HttpRequestMessage(HttpMethod.Get, Http.Query(api, WithFormat(p)))), ct);

    private Task<JsonNode> Post(Dictionary<string, string> p, CancellationToken ct) =>
        Http.SendJson(_client, () => Authorize(new HttpRequestMessage(HttpMethod.Post, api) { Content = new FormUrlEncodedContent(WithFormat(p)) }), ct);

    public async Task<string> Login(CancellationToken ct = default)
    {
        if (auth is CommonsAuth.BotPassword bot)
        {
            var t = await Get(new() { ["action"] = "query", ["meta"] = "tokens", ["type"] = "login" }, ct);
            var token = t["query"]?["tokens"]?["logintoken"]?.GetValue<string>() ?? "";
            var r = await Post(new() { ["action"] = "login", ["lgname"] = bot.Username, ["lgpassword"] = bot.Password, ["lgtoken"] = token }, ct);
            if (r["login"]?["result"]?.GetValue<string>() != "Success")
                throw new ApiException($"Oturum açılamadı: {r["login"]?["reason"]?.ToString() ?? r["login"]?["result"]?.ToString() ?? "bilinmeyen hata"}");
        }
        var d = await Get(new() { ["action"] = "query", ["meta"] = "userinfo|tokens", ["uiprop"] = "rights", ["type"] = "csrf" }, ct);
        if (d["error"]?["info"]?.GetValue<string>() is { } err) throw new ApiException($"Oturum açılamadı: {err}");
        var info = d["query"]?["userinfo"];
        if (info == null || info["anon"] != null) throw new ApiException("Oturum açılamadı: kimlik bilgisi kabul edilmedi");
        if (!(info["rights"]?.AsArray() ?? []).Any(x => x?.GetValue<string>() == "upload")) throw new ApiException("Bu kimlik bilgisinin dosya yükleme izni yok");
        _csrf = d["query"]?["tokens"]?["csrftoken"]?.GetValue<string>();
        return Username = info["name"]!.GetValue<string>();
    }

    /// <summary>Verilen sayfa başlıklarından hangilerinin var olduğunu döndürür.</summary>
    public async Task<Dictionary<string, bool>> Existing(IEnumerable<string> titles, CancellationToken ct = default)
    {
        var output = new Dictionary<string, bool>();
        foreach (var part in titles.Distinct().Order().Chunk(50))
        {
            var d = await Get(new() { ["action"] = "query", ["titles"] = string.Join("|", part) }, ct);
            var back = new Dictionary<string, string>();
            foreach (var n in d["query"]?["normalized"]?.AsArray() ?? []) back[n!["to"]!.GetValue<string>()] = n["from"]!.GetValue<string>();
            foreach (var p in d["query"]?["pages"]?.AsArray() ?? [])
            {
                var title = p!["title"]!.GetValue<string>();
                output[back.GetValueOrDefault(title, title)] = p["missing"] == null && p["invalid"] == null;
            }
        }
        return output;
    }

    private async Task<JsonNode> UploadCall(Dictionary<string, string> fields, byte[]? chunk, int chunkLength, CancellationToken ct)
    {
        var all = new Dictionary<string, string>(fields) { ["action"] = "upload", ["format"] = "json", ["formatversion"] = "2", ["token"] = _csrf ?? "" };
        var d = await Http.SendJson(_client, () =>
        {
            var form = new MultipartFormDataContent();
            foreach (var (k, v) in all) form.Add(new StringContent(v), k);
            if (chunk != null)
            {
                var content = new ByteArrayContent(chunk, 0, chunkLength);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(content, "chunk", "chunk");
            }
            return Authorize(new HttpRequestMessage(HttpMethod.Post, api) { Content = form });
        }, ct);
        if (d["error"]?["info"]?.GetValue<string>() is { } err) throw new ApiException(err);
        return d["upload"] ?? new JsonObject();
    }

    private static readonly string[] BlockingWarnings = ["exists", "exists-normalized", "duplicate", "duplicate-archive", "was-deleted", "badfilename"];

    private static string? Describe(JsonObject warnings)
    {
        var key = BlockingWarnings.FirstOrDefault(warnings.ContainsKey);
        return key switch
        {
            null => null,
            "duplicate" => $"Aynı dosya Commons'ta zaten var: {string.Join(", ", (warnings[key] as JsonArray ?? []).Select(x => x?.ToString()))}",
            "exists" or "exists-normalized" => "Bu adla bir dosya zaten var",
            "was-deleted" or "duplicate-archive" => "Bu dosya daha önce Commons'tan silinmiş",
            _ => "Dosya adı kabul edilmedi",
        };
    }

    /// <summary>Dosyayı parçalar hâlinde yükler. Engelleyici uyarı varsa yayımlamaz.</summary>
    public async Task<UploadOutcome> Upload(string file, string filename, string text, string comment, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (_csrf == null) throw new ApiException("Önce Commons oturumu açın");
        await using var stream = File.OpenRead(file);
        var size = stream.Length;
        long offset = 0;
        string? fileKey = null;
        var warnings = new JsonObject();
        var buffer = new byte[ChunkSize];
        while (offset < size)
        {
            ct.ThrowIfCancellationRequested();
            var read = await stream.ReadAtLeastAsync(buffer, ChunkSize, throwOnEndOfStream: false, ct);
            if (read == 0) break;
            var fields = new Dictionary<string, string> { ["stash"] = "1", ["filename"] = filename, ["filesize"] = size.ToString(), ["offset"] = offset.ToString() };
            if (fileKey != null) fields["filekey"] = fileKey;
            var r = await UploadCall(fields, buffer, read, ct);
            fileKey = r["filekey"]?.GetValue<string>() ?? fileKey;
            foreach (var (k, v) in r["warnings"]?.AsObject() ?? []) warnings[k] = v?.DeepClone();
            offset += read;
            progress?.Report((double)offset / Math.Max(size, 1));
        }
        if (Describe(warnings) is { } reason) return new UploadOutcome(false, BlockedReason: reason);
        if (fileKey == null) throw new ApiException("Yükleme başlatılamadı");
        var final = await UploadCall(new() { ["filekey"] = fileKey, ["filename"] = filename, ["text"] = text, ["comment"] = comment }, null, 0, ct);
        if (final["result"]?.GetValue<string>() != "Success")
        {
            if (final["warnings"] is JsonObject w) return new UploadOutcome(false, BlockedReason: Describe(w) ?? $"Uyarı: {string.Join(", ", w.Select(p => p.Key))}");
            throw new ApiException($"Yükleme tamamlanamadı: {final["result"]}");
        }
        var title = $"File:{final["filename"]?.GetValue<string>() ?? filename}";
        var q = await Get(new() { ["action"] = "query", ["titles"] = title, ["prop"] = "info", ["inprop"] = "url" }, ct);
        var page = q["query"]?["pages"]?[0];
        return new UploadOutcome(true, title, page?["pageid"]?.GetValue<long>(), page?["canonicalurl"]?.GetValue<string>());
    }

    /// <summary>Yapılandırılmış veri: dosyada görünen kişiler (P180).</summary>
    public async Task AddDepicts(long pageId, IEnumerable<string> qids, CancellationToken ct = default)
    {
        var claims = qids.Where(q => q.Length > 1 && long.TryParse(q.AsSpan(1), out _)).Select(q => new
        {
            type = "statement", rank = "normal",
            mainsnak = new { snaktype = "value", property = "P180", datavalue = new { type = "wikibase-entityid", value = new Dictionary<string, object> { ["entity-type"] = "item", ["numeric-id"] = long.Parse(q.AsSpan(1)), ["id"] = q } } },
        }).ToList();
        if (claims.Count == 0) return;
        var d = await Post(new() { ["action"] = "wbeditentity", ["id"] = $"M{pageId}", ["data"] = JsonSerializer.Serialize(new { claims }), ["token"] = _csrf ?? "", ["summary"] = "Görünen kişiler eklendi" }, ct);
        if (d["error"]?["info"]?.GetValue<string>() is { } err) throw new ApiException(err);
    }

    public async Task CreateCategory(string name, string text, CancellationToken ct = default)
    {
        var d = await Post(new() { ["action"] = "edit", ["title"] = $"Category:{name}", ["text"] = text, ["createonly"] = "1", ["summary"] = "Maç kategorisi oluşturuldu", ["token"] = _csrf ?? "" }, ct);
        if (d["error"] is { } e && e["code"]?.GetValue<string>() != "articleexists") throw new ApiException(e["info"]?.GetValue<string>() ?? "Kategori oluşturulamadı");
    }

    public void Dispose() => _client.Dispose();
}
