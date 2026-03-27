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
        var client = _httpFactory.CreateClient("embedding");

        var payload = JsonSerializer.Serialize(new { text });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"{baseUrl.TrimEnd('/')}/embed", content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Embedding service error ({(int)response.StatusCode}): {json}");

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("embedding", out var embeddingElement) ||
            embeddingElement.ValueKind != JsonValueKind.Array)
        {
            throw new Exception("Embedding service response did not contain a valid 'embedding' array.");
        }

        return embeddingElement
            .EnumerateArray()
            .Select(v => (float)v.GetDouble())
            .ToArray();
    }
}