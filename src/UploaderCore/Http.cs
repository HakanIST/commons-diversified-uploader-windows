using System.Net;
using System.Text.Json.Nodes;

namespace UploaderCore;

public class ApiException(string message) : Exception(message);

public static class Http
{
    public const string UserAgent = "CommonsDiversifiedUploader/0.1 (Windows; native app)";

    public static HttpClient CreateClient(bool cookies = false)
    {
        var handler = new SocketsHttpHandler { UseCookies = cookies, CookieContainer = new CookieContainer(), AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    public static readonly HttpClient Shared = CreateClient();

    public static string Query(string baseUrl, IEnumerable<KeyValuePair<string, string>> parameters) =>
        baseUrl + "?" + string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    /// <summary>Geçici hatalarda (429, 5xx, bağlantı) artan beklemeyle yeniden dener.</summary>
    public static async Task<JsonNode> SendJson(HttpClient client, Func<HttpRequestMessage> makeRequest, CancellationToken ct = default, int retries = 3)
    {
        Exception last = new ApiException("İstek başarısız");
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                using var response = await client.SendAsync(makeRequest(), ct);
                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) throw new ApiException($"HTTP {(int)response.StatusCode}");
                var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) ?? throw new ApiException("Beklenmeyen yanıt");
                var code = node["error"]?["code"]?.GetValue<string>();
                if (code is "maxlag" or "ratelimited") throw new ApiException(code);
                return node;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                last = e;
                if (attempt < retries) await Task.Delay(TimeSpan.FromSeconds(1.5 * (attempt + 1)), ct);
            }
        }
        throw last;
    }

    public static Task<JsonNode> GetJson(string baseUrl, Dictionary<string, string> parameters, HttpClient? client = null, CancellationToken ct = default) =>
        SendJson(client ?? Shared, () => new HttpRequestMessage(HttpMethod.Get, Query(baseUrl, parameters)), ct);
}
