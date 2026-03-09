using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class ChatOrchestrator
{
    private readonly FaqRepository _repo;
    private readonly ConversationMemory _memory;
    private readonly ChatScoringService _scoring;
    private readonly EmbeddingService _embed;
    private readonly RetrievalService _retrieval;
    private readonly IConfiguration _config;
    private readonly LlmService _llm;
    private readonly RoutingService _routing;
    private readonly SupportResourceRepository _supportResources;

    private readonly HashSet<string> _genericMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        "help","hi","hello","hey","ok","okay","thanks","thank you","please"
    };

    public ChatOrchestrator(
        FaqRepository repo,
        ConversationMemory memory,
        ChatScoringService scoring,
        EmbeddingService embed,
        RetrievalService retrieval,
        IConfiguration config,
        LlmService llm,
        RoutingService routing,
        SupportResourceRepository supportResources)
    {
        _repo = repo;
        _memory = memory;
        _scoring = scoring;
        _embed = embed;
        _retrieval = retrieval;
        _config = config;
        _llm = llm;
        _routing = routing;
        _supportResources = supportResources;
    }

    public async Task<(string reply, string service, string nextStepsUrl, float score)> HandleChatAsync(string sessionId, string message)
    {
        var lastService = _memory.GetLastService(sessionId) ?? "";
        var currentMode = _memory.GetMode(sessionId) ?? "";
        var normMsg = Normalize(message);

        Console.WriteLine($"ORCH: message='{message}'");
        Console.WriteLine($"ORCH: lastService='{lastService}', currentMode='{currentMode}'");

        if (_genericMessages.Contains(normMsg))
        {
            return (
                "Hi — I can help with Council Tax, Waste & Bins, Benefits & Support, and School Admissions. For mental health support I can only signpost trusted services. What do you need help with?",
                "Unknown",
                "",
                0
            );
        }

        var route = _routing.Classify(message, currentMode, lastService);
        _memory.SetMode(sessionId, route.Mode);

        Console.WriteLine($"ORCH: routeMode='{route.Mode}', routeService='{route.Service}', routeIntent='{route.Intent}'");

        if (route.Mode == "crisis")
        {
            return (
                "I’m really sorry you’re going through this. I’m not able to provide crisis support directly. If you might act on these thoughts now, call emergency services right away. You can also call NHS 111 and choose the mental health option. For urgent mental health support, First Response is available 24/7 on 0800 952 1181.",
                "Crisis Support",
                "",
                1.0f
            );
        }

        if (route.Mode == "signpost")
        {
            var resource = _supportResources.FindBestMatch(message);

            var reply =
                "I’m sorry you’re going through a difficult time. I’m not designed to provide mental health support directly. " +
                "If this is an emergency or you feel at immediate risk, call emergency services or NHS 111 and choose the mental health option. " +
                "For non-urgent mental health support in Bradford, please use this NHS resource page.";

            return (
                reply,
                "Mental Health Support",
                resource?.Url ?? "https://www.bdct.nhs.uk/our-services/mental-health-services/",
                1.0f
            );
        }

        if (route.Mode == "out_of_scope")
        {
            return (
                "I’m mainly set up for Bradford Council topics such as Council Tax, Waste & Bins, Benefits & Support, and School Admissions. If you need help with one of those, tell me which area. If you need mental health support, I can signpost trusted NHS services.",
                "Unknown",
                "",
                0.2f
            );
        }

        // FAQ mode only from here
        var threshold = _config.GetValue("Retrieval:Threshold", 0.55f);
        var qEmb = await _embed.EmbedAsync(message);
        var top = _retrieval.TopK(qEmb, 3);

        var best = top.FirstOrDefault();
        var faq = best.faq;
        var score = best.score;

        Console.WriteLine($"ORCH: faqBestScore={score}");

        if (faq == null)
        {
            return (
                "I can help with Council Tax, Waste & Bins, Benefits & Support, and School Admissions. What do you need help with?",
                "Unknown",
                "",
                0
            );
        }

        var candidates = top.Select(x => new LlmFaqCandidate
        {
            Service = x.faq.Service ?? "",
            Title = x.faq.Title ?? "",
            Answer = x.faq.Answer ?? "",
            Keywords = x.faq.Keywords ?? new List<string>(),
            Responses = x.faq.Responses ?? new List<string>(),
            NextStepsUrl = x.faq.NextStepsUrl ?? "",
            Score = x.score
        }).ToList();

        LlmDecisionResult decision;

        try
        {
            decision = await _llm.DecideNextStepAsync(
                userMessage: message,
                lastService: lastService,
                bestScore: score,
                threshold: threshold,
                candidates: candidates);

            Console.WriteLine($"ORCH: llmAction='{decision.Action}', llmService='{decision.Service}', llmIntent='{decision.Intent}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ORCH: LLM failed, using fallback. Error={ex.Message}");

            decision = new LlmDecisionResult
            {
                Action = score >= threshold ? "answer" : "clarify",
                Service = route.Service != "Unknown" ? route.Service : (faq.Service ?? "Unknown"),
                Intent = "",
                Reply = score >= threshold
                    ? _scoring.PickReply(faq)
                    : BuildFallbackClarification(route.Service != "Unknown" ? route.Service : (faq.Service ?? "Unknown"), candidates),
                NextStepsUrl = score >= threshold ? (faq.NextStepsUrl ?? "") : ""
            };
        }

        var finalService = route.Service != "Unknown"
            ? route.Service
            : (string.IsNullOrWhiteSpace(decision.Service) ? (faq.Service ?? "Unknown") : decision.Service);

        if (!string.IsNullOrWhiteSpace(finalService) && finalService != "Unknown")
            _memory.SetLastService(sessionId, finalService);

        // Only attach URL for grounded FAQ answers
        var safeUrl = decision.Action == "answer" && candidates.Any(c => c.NextStepsUrl == decision.NextStepsUrl)
            ? decision.NextStepsUrl
            : (decision.Action == "answer" ? (faq.NextStepsUrl ?? "") : "");

        return (decision.Reply, finalService, safeUrl, score);
    }

    private static string BuildFallbackClarification(string service, List<LlmFaqCandidate> candidates)
    {
        var titles = candidates
            .Where(c => c.Service.Equals(service, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .Take(3)
            .ToList();

        if (titles.Count == 0)
            return $"I can help with {service}. Could you tell me a bit more about what you need?";

        return $"I can help with {service}. Are you asking about {string.Join(", ", titles)}?";
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        input = input.ToLowerInvariant();
        var chars = input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}