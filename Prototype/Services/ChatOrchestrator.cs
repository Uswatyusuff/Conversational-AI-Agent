using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class ChatOrchestrator
{
    private readonly ConversationMemory _memory;
    private readonly EmbeddingService _embed;
    private readonly RetrievalService _retrieval;
    private readonly OpenAiChatService _openAi;
    private readonly IConfiguration _config;

    // Strong topic switch triggers
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

    // Generic chatter
    private readonly HashSet<string> _genericMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        "help","hi","hello","hey","ok","okay","thanks","thank you","please"
    };

    public ChatOrchestrator(
        ConversationMemory memory,
        EmbeddingService embed,
        RetrievalService retrieval,
        OpenAiChatService openAi,
        IConfiguration config)
    {
        _memory = memory;
        _embed = embed;
        _retrieval = retrieval;
        _openAi = openAi;
        _config = config;
    }

    public async Task<(string reply, string service, string nextStepsUrl, float score)> HandleChatAsync(string sessionId, string message)
    {
        var lastService = _memory.GetLastService(sessionId) ?? "";
        var normMsg = Normalize(message);

        // very generic -> ask service
        var detectedService = DetectService(normMsg, _strongServiceTriggers);
        if (_genericMessages.Contains(normMsg) && string.IsNullOrWhiteSpace(detectedService))
        {
            return ("I can help with **Council Tax**, **Waste/Bins**, **Benefits**, and **School Admissions**. Which service do you need?",
                "Unknown", "", 0);
        }

        // Embed query
        var qEmb = await _embed.EmbedAsync(message);

        // Threshold
        var threshold = _config.GetValue("Retrieval:Threshold", 0.50f);

        // Retrieve top chunks
        List<(FaqChunk chunk, float score)> top =
            !string.IsNullOrWhiteSpace(detectedService)
                ? _retrieval.TopKInService(qEmb, detectedService, 3)
                : _retrieval.TopK(qEmb, 3);

        var best = top.FirstOrDefault();
        var second = top.Skip(1).FirstOrDefault();

        var bestChunk = best.chunk;
        var bestScore = best.score;

        // If no good match, do follow-up logic
        if (bestChunk == null || bestScore < threshold)
        {
            var contextService =
                !string.IsNullOrWhiteSpace(detectedService) ? detectedService :
                !string.IsNullOrWhiteSpace(lastService) ? lastService :
                "Unknown";

            if (contextService != "Unknown")
                _memory.SetLastService(sessionId, contextService);

            if (contextService == "Unknown")
            {
                return ("Which service is this about: **Council Tax**, **Waste/Bins**, **Benefits**, or **School Admissions**?",
                    "Unknown", "", bestScore);
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

            return (tailored, contextService, "", bestScore);
        }

        // Optional disambiguation: if top2 are very close
        var close = second.chunk != null && Math.Abs(bestScore - second.score) <= 0.03f;
        if (close)
        {
            var reply =
                $"I found two close matches. Did you mean:\n" +
                $"- **1)** {best.chunk.Title} ({best.chunk.Service})\n" +
                $"- **2)** {second.chunk.Title} ({second.chunk.Service})\n\n" +
                "Reply with **1** or **2**, or rephrase your question.";

            _memory.SetLastService(sessionId, best.chunk.Service);
            return (reply, best.chunk.Service, "", bestScore);
        }

        // Matched: store service
        var finalService = bestChunk.Service ?? "Unknown";
        if (finalService != "Unknown")
            _memory.SetLastService(sessionId, finalService);

        // ✅ Use OpenAI to generate a nicer answer from top chunks
        var context = top
            .Where(t => t.chunk != null)
            .Select(t => (t.chunk.Title, t.chunk.Text, t.chunk.NextStepsUrl))
            .ToList();

        var aiReply = await _openAi.GenerateAnswerAsync(message, finalService, context);

        // Use best chunk’s next step URL (or empty)
        return (aiReply, finalService, bestChunk.NextStepsUrl ?? "", bestScore);
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