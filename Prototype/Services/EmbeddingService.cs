using System.Net;
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

    public async Task<bool> HealthAsync(CancellationToken ct = default)
    {
        var baseUrl = GetBaseUrl();
        var client = _httpFactory.CreateClient("embeddings");

        try
        {
            var res = await client.GetAsync($"{baseUrl}/health", ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<float>();

        var baseUrl = GetBaseUrl();
        var client = _httpFactory.CreateClient("embeddings");

        var payload = JsonSerializer.Serialize(new { text });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        // Retry policy (simple, no external libs):
        // Handles: connection refused, timeouts, 502/503/504, and startup lag.
        var maxAttempts = GetInt("EmbeddingService:MaxRetries", 5);
        var baseDelayMs = GetInt("EmbeddingService:RetryDelayMs", 300);

        Exception? lastEx = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/embed")
                {
                    Content = content
                };

                // IMPORTANT: recreate content per attempt because HttpContent can be disposed.
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var res = await client.SendAsync(req, ct);
                var json = await res.Content.ReadAsStringAsync(ct);

                if (!res.IsSuccessStatusCode)
                {
                    // Retry on transient server errors
                    if (IsTransientStatus(res.StatusCode) && attempt < maxAttempts)
                    {
                        await Task.Delay(Backoff(baseDelayMs, attempt), ct);
                        continue;
                    }

                    throw new Exception($"Embedding service failed ({(int)res.StatusCode}): {json}");
                }

                // Parse response
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("embedding", out var emb) || emb.ValueKind != JsonValueKind.Array)
                    throw new Exception($"Embedding service returned invalid JSON (missing 'embedding'): {json}");

                var floats = new float[emb.GetArrayLength()];
                int i = 0;
                foreach (var v in emb.EnumerateArray())
                {
                    // numbers might come back as double; GetSingle can throw on some JSON numbers
                    floats[i++] = (float)v.GetDouble();
                }

                return floats;
            }
            catch (HttpRequestException ex)
            {
                lastEx = ex;
                if (attempt < maxAttempts)
                {
                    await Task.Delay(Backoff(baseDelayMs, attempt), ct);
                    continue;
                }
            }
            catch (TaskCanceledException ex) // timeout or cancellation
            {
                lastEx = ex;
                if (attempt < maxAttempts)
                {
                    await Task.Delay(Backoff(baseDelayMs, attempt), ct);
                    continue;
                }
            }
            catch (JsonException ex)
            {
                // JSON errors usually won't be fixed by retrying, but keep 1 retry just in case
                lastEx = ex;
                if (attempt < Math.Min(2, maxAttempts))
                {
                    await Task.Delay(Backoff(baseDelayMs, attempt), ct);
                    continue;
                }
                break;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                break;
            }
        }

        throw new Exception(
            "Could not reach the local embedding service. " +
            "Make sure the Python server is running:\n" +
            "  python -m uvicorn app:app --reload --host 127.0.0.1 --port 8001\n" +
            $"BaseUrl: {_config["EmbeddingService:BaseUrl"]}\n" +
            $"Last error: {lastEx?.Message}",
            lastEx
        );
    }

    // ---------------- helpers ----------------

    private string GetBaseUrl()
    {
        var baseUrl = _config["EmbeddingService:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new Exception("Missing config: EmbeddingService:BaseUrl in appsettings.json");

        return baseUrl.TrimEnd('/');
    }

    private static bool IsTransientStatus(HttpStatusCode code)
        => code == HttpStatusCode.BadGateway
        || code == HttpStatusCode.ServiceUnavailable
        || code == HttpStatusCode.GatewayTimeout;

    private static int Backoff(int baseDelayMs, int attempt)
    {
        // exponential backoff with small cap
        var ms = baseDelayMs * (int)Math.Pow(2, attempt - 1);
        return Math.Min(ms, 2500);
    }

    private int GetInt(string key, int fallback)
    {
        var raw = _config[key];
        return int.TryParse(raw, out var val) ? val : fallback;
    }
}