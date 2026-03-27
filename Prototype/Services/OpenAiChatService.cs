using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CouncilChatbotPrototype.Services;

public class OpenAiChatService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;

    public OpenAiChatService(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _config = config;
    }

    public async Task<string> GenerateAnswerAsync(
        string userMessage,
        string service,
        List<(string title, string text, string nextUrl)> contextChunks,
        CancellationToken ct = default)
    {
        var model = _config["OpenAI:ChatModel"] ?? "gpt-4.1-mini";
        var apiKey = _config["OpenAI:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
            return "The AI service is not configured yet. Please contact the administrator.";

        var client = _httpFactory.CreateClient("openai");

        var contextText = string.Join("\n\n", contextChunks.Select((c, i) =>
            $"[Chunk {i + 1}] Title: {c.title}\nText: {c.text}\nNext: {c.nextUrl}"));

        var instructions =
            "You are a helpful Bradford Council services assistant (prototype).\n" +
            "Rules:\n" +
            "- Do NOT ask for or store personal data.\n" +
            "- Answer using ONLY the provided context chunks.\n" +
            "- If context is insufficient, ask ONE clarifying question.\n" +
            "- Keep answers short (2–5 sentences).\n" +
            "- If you mention a next step, prefer the provided Next URL.\n";

        var payload = JsonSerializer.Serialize(new
        {
            model,
            instructions,
            input = new[]
            {
                new
                {
                    role = "user",
                    content =
                        $"Service: {service}\n\n" +
                        $"Context:\n{contextText}\n\n" +
                        $"Question: {userMessage}"
                }
            },
            text = new { format = new { type = "text" } }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, "responses");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var res = await client.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            if ((int)res.StatusCode == 429)
            {
                return "The AI response service is temporarily unavailable because the API quota has been exceeded. Please try again later.";
            }

            return $"The AI response service is currently unavailable. Status: {(int)res.StatusCode}.";
        }

        var answer = ExtractOutputText(json);

        return string.IsNullOrWhiteSpace(answer)
            ? "Sorry — I couldn’t generate an answer from the available information. Can you rephrase your question?"
            : answer.Trim();
    }

    private static string ExtractOutputText(string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
            return "";

        var sb = new StringBuilder();

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var c in content.EnumerateArray())
            {
                if (c.TryGetProperty("type", out var t) &&
                    t.GetString() == "output_text" &&
                    c.TryGetProperty("text", out var textEl))
                {
                    sb.AppendLine(textEl.GetString());
                }
            }
        }

        return sb.ToString();
    }
}