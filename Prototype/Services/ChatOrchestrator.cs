using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class ChatOrchestrator
{
    private readonly ConversationMemory _memory;
    private readonly EmbeddingService _embed;
    private readonly RetrievalService _retrieval;
    private readonly OpenAiChatService _openAi;
    private readonly LangChainClientService _langChain;
    private readonly IConfiguration _config;

    private readonly Dictionary<string, string[]> _strongServiceTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Council Tax"] = new[]
        {
            "council tax", "ctax", "tax", "bill", "balance", "arrears",
            "direct debit", "discount", "exemption", "council tax payment"
        },
        ["Waste & Bins"] = new[]
        {
            "bin", "bins", "waste", "recycling", "missed", "collection",
            "bulky", "replacement bin", "bin collection"
        },
        ["Benefits & Support"] = new[]
        {
            "benefit", "benefits", "support", "financial support", "hardship",
            "council tax support", "housing benefit", "universal credit", "uc",
            "money help", "blue badge", "disabled badge", "disable badge",
            "mobility support", "parking badge"
        },
        ["Education"] = new[]
        {
            "school", "schools", "admissions", "apply for school", "deadline",
            "in-year", "transfer", "send", "ehcp", "transport", "school place"
        }
    };

    private readonly HashSet<string> _genericMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        "help", "hi", "hello", "hey", "ok", "okay", "thanks", "thank you", "please"
    };

    public ChatOrchestrator(
        ConversationMemory memory,
        EmbeddingService embed,
        RetrievalService retrieval,
        OpenAiChatService openAi,
        LangChainClientService langChain,
        IConfiguration config)
    {
        _memory = memory;
        _embed = embed;
        _retrieval = retrieval;
        _openAi = openAi;
        _langChain = langChain;
        _config = config;
    }

    public async Task<(string reply, string service, string nextStepsUrl, float score)> HandleChatAsync(string sessionId, string message)
    {
        var normMsg = Normalize(message);
        var lastService = _memory.GetLastService(sessionId) ?? "";

        // 1. Generic greetings / vague starter prompts
        if (_genericMessages.Contains(normMsg))
        {
            var genericReply =
                "I can help with **Council Tax**, **Waste/Bins**, **Benefits & Support**, and **School Admissions**. What would you like help with?";
            SaveConversation(sessionId, message, genericReply, "Unknown");
            return (genericReply, "Unknown", "", 0);
        }

        // 2. Use light service hinting only
        var detectedService = DetectService(normMsg, _strongServiceTriggers);

        // 3. Embed query
        var qEmb = await _embed.EmbedAsync(message);
        var threshold = _config.GetValue("Retrieval:Threshold", 0.45f);

        // 4. Retrieve candidate chunks
        List<(FaqChunk chunk, float score)> top =
            !string.IsNullOrWhiteSpace(detectedService)
                ? _retrieval.TopKInService(qEmb, detectedService, 4)
                : _retrieval.TopK(qEmb, 4);

        var best = top.FirstOrDefault();
        var bestChunk = best.chunk;
        var bestScore = best.score;

        // 5. Build context even if retrieval is weak
        var context = top
            .Where(t => t.chunk != null)
            .Select(t => (
                title: t.chunk.Title ?? "",
                text: t.chunk.Text ?? "",
                nextUrl: t.chunk.NextStepsUrl ?? ""
            ))
            .ToList();

        var history = _memory.GetRecentTurns(sessionId, 6)
            .Select(t => (role: t.Role ?? "user", message: t.Message ?? ""))
            .ToList();

        // 6. Build service hint for the agent
        var serviceHint =
            !string.IsNullOrWhiteSpace(detectedService) ? detectedService :
            !string.IsNullOrWhiteSpace(lastService) ? lastService :
            bestChunk?.Service ?? "Unknown";

        // 7. If retrieval is weak, still let the agent try first
        //    This is important for typo tolerance, vague prompts, and tool-calling
        if (bestChunk == null || bestScore < threshold)
        {
            var weakContextAgentResult = await _langChain.RunAgentAsync(message, serviceHint, context, history);

            var weakReply = weakContextAgentResult.answer;
            var weakService = string.IsNullOrWhiteSpace(weakContextAgentResult.service)
                ? serviceHint
                : weakContextAgentResult.service;
            var weakNextStepsUrl = weakContextAgentResult.nextStepsUrl ?? "";

            if (!string.IsNullOrWhiteSpace(weakReply) &&
                !weakReply.Contains("I’m not sure", StringComparison.OrdinalIgnoreCase) &&
                !weakReply.Contains("not configured", StringComparison.OrdinalIgnoreCase))
            {
                SaveConversation(sessionId, message, weakReply, weakService);
                return (weakReply, weakService, weakNextStepsUrl, bestScore);
            }

            var clarificationReply =
                "I’m not fully sure which council service this is about yet. Can you tell me whether it relates to **Council Tax**, **Bins/Waste**, **Benefits & Support**, or **School Admissions**?";
            SaveConversation(sessionId, message, clarificationReply, "Unknown");
            return (clarificationReply, "Unknown", "", bestScore);
        }

        // 8. Strong retrieved service
        var finalService = string.IsNullOrWhiteSpace(bestChunk.Service) ? "Unknown" : bestChunk.Service;
        _memory.SetLastService(sessionId, finalService);

        // 9. Let the LangChain agent decide answer / tool use
        var agentResult = await _langChain.RunAgentAsync(message, finalService, context, history);

        var aiReply = agentResult.answer;
        var resolvedService = string.IsNullOrWhiteSpace(agentResult.service) ? finalService : agentResult.service;
        var resolvedNextStepsUrl = string.IsNullOrWhiteSpace(agentResult.nextStepsUrl)
            ? (bestChunk.NextStepsUrl ?? "")
            : agentResult.nextStepsUrl;

        // 10. Fallback to direct OpenAI if agent returns nothing
        if (string.IsNullOrWhiteSpace(aiReply))
        {
            aiReply = await _openAi.GenerateAnswerAsync(message, finalService, context);
        }

        // 11. Final fallback to retrieved text
        if (string.IsNullOrWhiteSpace(aiReply))
        {
            aiReply = bestChunk.Text ?? "Sorry — I could not find a reliable answer from the available council information.";
        }

        SaveConversation(sessionId, message, aiReply, resolvedService);

        return (aiReply, resolvedService, resolvedNextStepsUrl, bestScore);
    }

    private void SaveConversation(string sessionId, string userMessage, string assistantReply, string service)
    {
        _memory.AddTurn(sessionId, "user", userMessage);
        _memory.AddTurn(sessionId, "assistant", assistantReply);
        _memory.SetLastService(sessionId, service);
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        input = input.ToLowerInvariant();

        // Simple typo / wording normalization
        input = input.Replace("badg", "badge");
        input = input.Replace("disabl", "disabled");
        input = input.Replace("bin day", "bin collection");
        input = input.Replace("c tax", "council tax");

        var chars = input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string DetectService(string normMsg, Dictionary<string, string[]> triggers)
    {
        foreach (var kv in triggers)
        {
            foreach (var trigger in kv.Value)
            {
                var normalizedTrigger = Normalize(trigger);
                if (!string.IsNullOrWhiteSpace(normalizedTrigger) && normMsg.Contains(normalizedTrigger))
                    return kv.Key;
            }
        }

        return "";
    }
}