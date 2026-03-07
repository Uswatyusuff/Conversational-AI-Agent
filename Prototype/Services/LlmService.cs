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

    public async Task<LlmReplyResult> GenerateReplyAsync(
        string userMessage,
        string lastService,
        List<LlmFaqCandidate> candidates,
        CancellationToken ct = default)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return new LlmReplyResult
            {
                Reply = "I can help with Council Tax, Waste/Bins, Benefits, and School Admissions. Which service do you need?",
                Service = "Unknown",
                NextStepsUrl = ""
            };
        }

        var payload = new
        {
            userMessage,
            lastService,
            faqCandidates = candidates
        };

        var prompt = """
You are a council support assistant.

Your job is to answer the user's question using ONLY the FAQ candidates provided.
Do not invent policies, links, eligibility rules, deadlines, or contact details.
Use the FAQ candidates as the source of truth.

Rules:
- Give a short, clear, friendly answer.
- Prefer the most relevant FAQ candidate.
- If multiple candidates are relevant, combine them carefully but do not invent new facts.
- Keep the answer grounded in the provided FAQ text.
- The service must be one of the provided candidate services.
- The nextStepsUrl must be one of the provided candidate URLs or empty.
- Return raw JSON only.
- Do not use markdown.
- Do not wrap the JSON in ```json fences.
- Do not include any explanation before or after the JSON.
- Use this exact shape:
  {
    "Reply": "string",
    "Service": "string",
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
        Console.WriteLine("LLM: Sending request to OpenAI...");

        ResponseResult response = await _client.CreateResponseAsync(options, ct);

        Console.WriteLine("LLM: Received response from OpenAI.");
        var text = response.GetOutputText()?.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            Console.WriteLine("LLM: OpenAI returned empty output. Using fallback.");
            return Fallback(candidates);
        }

        text = CleanJson(text);

        try
        {
            var result = JsonSerializer.Deserialize<LlmReplyResult>(
                text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null || string.IsNullOrWhiteSpace(result.Reply)){
                Console.WriteLine("LLM: Parsed result invalid. Using fallback.");
                return Fallback(candidates);
                }
            Console.WriteLine($"LLM: Parsed reply successfully. Service={result.Service}");
            return new LlmReplyResult
            {
                Reply = result.Reply,
                Service = string.IsNullOrWhiteSpace(result.Service)
                    ? candidates[0].Service
                    : result.Service,
                NextStepsUrl = string.IsNullOrWhiteSpace(result.NextStepsUrl)
                    ? candidates[0].NextStepsUrl
                    : result.NextStepsUrl
            };
        }
        catch
        {
            
            return Fallback(candidates);
        }
    }

    private static LlmReplyResult Fallback(List<LlmFaqCandidate> candidates)
    {
        var best = candidates[0];

        return new LlmReplyResult
        {
            Reply = best.Answer,
            Service = best.Service,
            NextStepsUrl = best.NextStepsUrl
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