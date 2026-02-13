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

    // Strong service triggers (topic switch override)
    private readonly Dictionary<string, string[]> _strongServiceTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Council Tax"] = new[] { "council tax", "ctax", "tax", "bill", "balance", "arrears", "direct debit", "discount", "exemption" },
        ["Waste & Bins"] = new[] { "bin", "bins", "waste", "recycling", "missed", "collection", "bulky", "replacement bin" },
        ["Benefits & Support"] = new[] { "benefit", "benefits", "support", "financial support", "hardship", "council tax support", "housing benefit", "universal credit", "uc", "money help" },
        ["Education"] = new[] { "school", "admissions", "apply for school", "deadline", "in-year", "transfer", "send", "ehcp", "transport" }
    };

    // Follow-up intent labels
    private readonly Dictionary<string, string[]> _followUpIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["payment"] = new[] { "pay", "payment", "paying", "missed payment", "owe", "arrears", "direct debit" },
        ["eligibility"] = new[] { "eligible", "eligibility", "qualify", "can i get", "who can", "discount", "exemption" },
        ["application"] = new[] { "apply", "application", "how do i apply", "form", "submit" },
        ["contact"] = new[] { "contact", "phone", "email", "speak to", "call", "talk to someone" }
    };

    // Very generic (avoid forcing follow-up)
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
        IConfiguration config)
    {
        _repo = repo;
        _memory = memory;
        _scoring = scoring;
        _embed = embed;
        _retrieval = retrieval;
        _config = config;
    }

    public async Task<(string reply, string service, string nextStepsUrl, float score)> HandleChatAsync(string sessionId, string message)
    {
        var lastService = _memory.GetLastService(sessionId) ?? "";
        var normMsg = Normalize(message);

        // If message is extremely generic and no strong service -> ask for service
        var detectedService = DetectService(normMsg, _strongServiceTriggers);

        if (_genericMessages.Contains(normMsg) && string.IsNullOrWhiteSpace(detectedService))
        {
            return ("I can help with **Council Tax**, **Waste/Bins**, **Benefits**, and **School Admissions**. Which service do you need?",
                "Unknown", "", 0);
        }

        // Semantic retrieve
        var threshold = _config.GetValue("Retrieval:Threshold", 0.55f);
        var qEmb = await _embed.EmbedAsync(message);
        var (faq, score) = _retrieval.BestMatch(qEmb);

        // If weak match, use follow-up logic (but allow topic switch)
        if (faq == null || score < threshold)
        {
            // Choose context: detectedService > lastService > Unknown
            var contextService = !string.IsNullOrWhiteSpace(detectedService)
                ? detectedService
                : (!string.IsNullOrWhiteSpace(lastService) ? lastService : "Unknown");

            if (contextService != "Unknown")
                _memory.SetLastService(sessionId, contextService);

            if (contextService == "Unknown")
            {
                return ("Which service is this about: **Council Tax**, **Waste/Bins**, **Benefits**, or **School Admissions**?",
                    "Unknown", "", score);
            }

            var followType = DetectFollowUpType(normMsg, _followUpIntents);

            var tailored = followType switch
            {
                "payment" => $"Is this about **paying** for **{contextService}** (e.g., instalments, missed payments, direct debit)?",
                "contact" => $"Do you want **contact details** for **{contextService}**, or should I link you to the official support page?",
                "application" => $"Are you asking how to **apply** for something under **{contextService}**? Tell me what you’re applying for.",
                "eligibility" => $"Are you checking **eligibility** for **{contextService}** (who qualifies / what documents are needed)?",
                _ => $"It looks like a follow-up about **{contextService}**. Can you clarify: **payment**, **eligibility**, **application**, or **contact details**?"
            };

            return (tailored, contextService, "", score);
        }

        // Matched FAQ
        var finalService = faq.Service ?? "Unknown";
        if (finalService != "Unknown")
            _memory.SetLastService(sessionId, finalService);

        var replyText = _scoring.PickReply(faq);
        var nextUrl = faq.NextStepsUrl ?? "";

        return (replyText, finalService, nextUrl, score);
    }

    // ---------------- Helpers ----------------

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        input = input.ToLowerInvariant();
        var chars = input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string DetectService(string normMsg, Dictionary<string, string[]> triggers)
    {
        foreach (var kv in triggers)
        {
            foreach (var t in kv.Value)
            {
                var tt = Normalize(t);
                if (!string.IsNullOrWhiteSpace(tt) && normMsg.Contains(tt))
                    return kv.Key;
            }
        }
        return "";
    }

    private static string DetectFollowUpType(string normMsg, Dictionary<string, string[]> intents)
    {
        foreach (var kv in intents)
        {
            foreach (var w in kv.Value)
            {
                var ww = Normalize(w);
                if (!string.IsNullOrWhiteSpace(ww) && normMsg.Contains(ww))
                    return kv.Key;
            }
        }
        return "";
    }
}