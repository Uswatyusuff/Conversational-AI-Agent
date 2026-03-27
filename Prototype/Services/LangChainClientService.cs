using System.Text;
using System.Text.Json;

namespace CouncilChatbotPrototype.Services;

public class LangChainClientService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;

    public LangChainClientService(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _config = config;
    }

    public async Task<(string answer, string service, string action, bool needsClarification, string toolUsed, string nextStepsUrl)>
        RunAgentAsync(
            string userMessage,
            string serviceHint,
            List<(string title, string text, string nextUrl)> contextChunks,
            List<(string role, string message)> history,
            CancellationToken ct = default)
    {
        var baseUrl = _config["LangChain:BaseUrl"] ?? "http://127.0.0.1:8010";
        var client = _httpFactory.CreateClient("langchain");

        var payload = new
        {
            question = userMessage,
            service_hint = serviceHint,
            context_chunks = contextChunks.Select(c => new
            {
                title = c.title,
                text = c.text,
                nextUrl = c.nextUrl
            }).ToList(),
            history = history.Select(h => new
            {
                role = h.role,
                message = h.message
            }).ToList()
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await client.PostAsync($"{baseUrl.TrimEnd('/')}/agent", content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return ("", serviceHint, "error", false, "", "");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return (
            root.TryGetProperty("answer", out var answer) ? answer.GetString() ?? "" : "",
            root.TryGetProperty("service", out var service) ? service.GetString() ?? "Unknown" : "Unknown",
            root.TryGetProperty("action", out var action) ? action.GetString() ?? "answer" : "answer",
            root.TryGetProperty("needs_clarification", out var nc) && nc.GetBoolean(),
            root.TryGetProperty("tool_used", out var tool) ? tool.GetString() ?? "" : "",
            root.TryGetProperty("next_steps_url", out var next) ? next.GetString() ?? "" : ""
        );
    }
}