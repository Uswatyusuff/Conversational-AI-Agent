using System.Text;
using System.Text.Json;

namespace CouncilChatbotPrototype.Services;

public class EmbeddingService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;

    public EmbeddingService(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _config = config;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<float>();

        var baseUrl = _config["EmbeddingService:BaseUrl"] ?? "http://127.0.0.1:8001";
        var client = _httpFactory.CreateClient();

        var payload = JsonSerializer.Serialize(new { text });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var res = await client.PostAsync($"{baseUrl}/embed", content, ct);
        var json = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new Exception($"Embedding service error: {json}");

        using var doc = JsonDocument.Parse(json);
        var emb = doc.RootElement.GetProperty("embedding");

        return emb.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
    }
}