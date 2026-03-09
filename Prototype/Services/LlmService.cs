#pragma warning disable OPENAI001
using System.Text.Json;
using CouncilChatbotPrototype.Models;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace CouncilChatbotPrototype.Services;

public class LlmService
{
    private readonly ResponsesClient _client;
    private readonly OpenAiOptions _options;

    public LlmService(IOptions<OpenAiOptions> options)
    {
        _options = options.Value;

        var apiKey = !string.IsNullOrWhiteSpace(_options.ApiKey)
            ? _options.ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new Exception("Missing OpenAI API key. Set OpenAI:ApiKey or OPENAI_API_KEY.");

        _client = new ResponsesClient(apiKey);
    }

    public async Task<LlmDecisionResult> DecideNextStepAsync(
        string userMessage,
        string lastService,
        float bestScore,
        float threshold,
        List<LlmFaqCandidate> candidates,
        CancellationToken ct = default)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return new LlmDecisionResult
            {
                Action = "clarify",
                Service = "Unknown",
                Intent = "",
                Reply = "I can help with Council Tax, Waste & Bins, Benefits & Support, and School Admissions. What do you need help with?",
                NextStepsUrl = ""
            };
        }

        var payload = new
        {
            userMessage,
            lastService,
            bestScore,
            threshold,
            faqCandidates = candidates
        };

        var prompt = """
You are a council support assistant.

Your job is to decide the best next step for the user's message.

Allowed actions:
- "answer" = answer directly using the FAQ evidence
- "clarify" = ask a natural clarification question when evidence is weak, vague, or ambiguous

Rules:
- Use only the FAQ candidates provided.
- Do not invent services, URLs, policies, or facts.
- If bestScore is below threshold, usually choose "clarify".
- If clarifying, ask a short natural question that reflects the most likely services/intents.
- Avoid robotic clarification such as "payment, eligibility, application, contact details" unless that is clearly the best fit.
- Prefer clarifications grounded in actual FAQ topics.
- The service must be one of the candidate services or "Unknown".
- The nextStepsUrl must be one of the candidate URLs or empty.
- The intent should be a short snake_case label if possible.

Return raw JSON only.
Do not use markdown.
Do not wrap the JSON in code fences.

Use this exact shape:
{
  "Action": "answer or clarify",
  "Service": "string",
  "Intent": "string",
  "Reply": "string",
  "NextStepsUrl": "string"
}
""";

        var input = $"""
{prompt}

DATA:
{JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })}
""";

        CreateResponseOptions options = new()
        {
            Model = _options.Model
        };

        options.InputItems.Add(ResponseItem.CreateUserMessageItem(input));

        Console.WriteLine("LLM: Sending decision request to OpenAI...");
        ResponseResult response = await _client.CreateResponseAsync(options, ct);
        Console.WriteLine("LLM: Received decision response from OpenAI.");

        var text = response.GetOutputText()?.Trim();

        if (string.IsNullOrWhiteSpace(text))
            return FallbackDecision(bestScore, threshold, candidates);

        text = CleanJson(text);
        Console.WriteLine($"LLM raw output: {text}");

        try
        {
            var result = JsonSerializer.Deserialize<LlmDecisionResult>(
                text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null || string.IsNullOrWhiteSpace(result.Action) || string.IsNullOrWhiteSpace(result.Reply))
                return FallbackDecision(bestScore, threshold, candidates);

            return new LlmDecisionResult
            {
                Action = NormalizeAction(result.Action),
                Service = string.IsNullOrWhiteSpace(result.Service) ? candidates[0].Service : result.Service,
                Intent = result.Intent ?? "",
                Reply = result.Reply,
                NextStepsUrl = string.IsNullOrWhiteSpace(result.NextStepsUrl)
                    ? candidates[0].NextStepsUrl
                    : result.NextStepsUrl
            };
        }
        catch
        {
            return FallbackDecision(bestScore, threshold, candidates);
        }
    }

    private static LlmDecisionResult FallbackDecision(
        float bestScore,
        float threshold,
        List<LlmFaqCandidate> candidates)
    {
        var best = candidates[0];

        if (bestScore >= threshold)
        {
            return new LlmDecisionResult
            {
                Action = "answer",
                Service = best.Service,
                Intent = "",
                Reply = best.Answer,
                NextStepsUrl = best.NextStepsUrl
            };
        }

        return new LlmDecisionResult
        {
            Action = "clarify",
            Service = best.Service,
            Intent = "",
            Reply = BuildClarificationFromCandidates(candidates),
            NextStepsUrl = ""
        };
    }

    private static string BuildClarificationFromCandidates(List<LlmFaqCandidate> candidates)
    {
        var service = candidates[0].Service;

        var titles = candidates
            .Select(c => c.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .Take(3)
            .ToList();

        if (titles.Count == 0)
            return $"I can help with {service}. Could you tell me a bit more about what you need?";

        return $"I can help with {service}. Are you asking about {string.Join(", ", titles)}?";
    }

    private static string NormalizeAction(string action)
    {
        var a = (action ?? "").Trim().ToLowerInvariant();
        return a switch
        {
            "answer" => "answer",
            "clarify" => "clarify",
            _ => "clarify"
        };
    }

    private static string CleanJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = text.Trim();

        if (text.StartsWith("```"))
        {
            var lines = text.Split('\n').ToList();

            if (lines.Count > 0 && lines[0].TrimStart().StartsWith("```"))
                lines.RemoveAt(0);

            if (lines.Count > 0 && lines[^1].TrimStart().StartsWith("```"))
                lines.RemoveAt(lines.Count - 1);

            text = string.Join("\n", lines).Trim();
        }

        return text;
    }
}