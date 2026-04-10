using System.Text.RegularExpressions;
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
            "bulky", "replacement bin", "bin collection", "bin day",
            "collection day", "waste collection", "recycling collection"
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
        },["Planning"] = new[]
        
        {
            "planning", "planning application", "planning applications",
            "check planning application", "view planning application",
        "comment on planning application", "object to planning application",
        "planning permission", "building control"
        },
        ["Libraries"] = new[]
        {
            "library", "libraries", "renew library books", "renew books",
            "borrow books", "reserve books", "digital library", "e-books", "ebooks"
        },
        ["Housing"] = new[]
        {
            "housing", "homeless", "homelessness", "find a home",
            "repairs", "tenant", "landlord", "housing assistance"
        },
        ["Contact Us"] = new[]
        {
            "contact us", "contact the council", "telephone", "phone number",
            "email alerts", "call the council", "contact details"
        },
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

    public async Task<(string reply, string service, string nextStepsUrl, float score, List<string> suggestions)> HandleChatAsync(string sessionId, string message)
    {
        var normMsg = Normalize(message);
        var lastService = _memory.GetLastService(sessionId) ?? "";
        var lastIntent = _memory.GetLastIntent(sessionId) ?? "";
        var pendingFlow = _memory.GetPendingFlow(sessionId) ?? "";
        var maskedPostcode = _memory.GetMaskedPostcode(sessionId) ?? "";
        var maskedAddress = _memory.GetMaskedAddress(sessionId) ?? "";
        var activeAddress = _memory.GetActiveAddress(sessionId);
        var activePostcode = _memory.GetActivePostcode(sessionId);
        var lastBinResult = _memory.GetLastBinResult(sessionId);
        var hasAddress = _memory.GetHasSelectedAddress(sessionId);

        // 1. Generic greetings / vague starter prompts
        if (_genericMessages.Contains(normMsg))
        {
            var genericReply =
                "I can help with Council Tax, Waste and Bins, Benefits and Support, and School Admissions. What would you like help with?";
            var greetingSuggestions = new List<string>
            {
                "How do I check my Council Tax balance?",
                "When is my bin collection?",
                "How do I apply for a Blue Badge?",
                "How do I apply for free school meals?"
            };

            SaveConversation(sessionId, message, genericReply, "Unknown", "greeting", greetingSuggestions);
            return (genericReply, "Unknown", "", 0, greetingSuggestions);
        }

        // 3. Reset conversational context when user clearly changes topic
if (IsContextResetIntent(normMsg))
{
    _memory.ClearPendingFlow(sessionId);
    _memory.ClearAddressContext(sessionId);
    _memory.SetHasSelectedAddress(sessionId, false);
    _memory.SetLastBinResult(sessionId, "");

    var reply = "Of course. What would you like to ask about next?";
    var resetSuggestions = new List<string>
    {
        "Council Tax",
        "Waste & Bins",
        "Benefits & Support",
        "Plannning"
    };

    SaveConversation(sessionId, message, reply, "Unknown", "context_reset", resetSuggestions);
    return (reply, "Unknown", "", 1.0f, resetSuggestions);
}

        // 2. Continue short follow-up with previous service context
        if (string.IsNullOrWhiteSpace(DetectService(normMsg, _strongServiceTriggers)) &&
            IsShortFollowUp(normMsg) &&
            !string.IsNullOrWhiteSpace(lastService))
        {
            normMsg = $"{Normalize(lastService)} {normMsg}";
        }
                // 3. Use the previously selected address
        if (IsSameAddressIntent(normMsg) &&
            hasAddress &&
            !string.IsNullOrWhiteSpace(lastBinResult))
        {
            var reply = $"Here are the bin collection details for your previously selected address:\n\n{lastBinResult}";

            var sameAddressSuggestions = new List<string>
            {
                "Tell me about general waste",
                "Tell me about recycling",
                "Tell me about garden waste",
                "Use different address"
            };

            SaveConversation(sessionId, message, reply, "Waste & Bins", "bin_result_follow_up", sameAddressSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, sameAddressSuggestions);
        }

        // 4. Use a different address
        if (IsDifferentAddressIntent(normMsg))
        {
            _memory.SetPendingFlow(sessionId, "awaiting_postcode_for_bin_collection");

            var reply = "Please enter a different postcode so I can look up another address.";

            var differentAddressSuggestions = new List<string>
            {
                "Enter postcode"
            };

            SaveConversation(sessionId, message, reply, "Waste & Bins", "new_postcode", differentAddressSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, differentAddressSuggestions);
        }
        
        if (IsEnterPostcodeIntent(normMsg) &&
            string.Equals(lastService, "Waste & Bins", StringComparison.OrdinalIgnoreCase))
        {
            _memory.SetPendingFlow(sessionId, "awaiting_postcode_for_bin_collection");

            var reply = "Please enter your postcode so I can look up the address options for your bin collection day.";
            var postcodePromptSuggestions = new List<string>
            {
                "BD3 8PX"
            };

            SaveConversation(sessionId, message, reply, "Waste & Bins", "postcode_prompt", postcodePromptSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, postcodePromptSuggestions);
        }
        if (IsMissedBinIntent(normMsg))
{
    var reply = "To report a missed bin, fill in the missed bin form or call 01274 431000. You may need to register before using the form.";
    var suggestions = new List<string>
    {
        "When is my bin collection?",
        "Report a missed bin",
        "Request a new bin"
    };

    SaveConversation(sessionId, message, reply, "Waste & Bins", "missed_bin", suggestions);
    return (reply, "Waste & Bins", "", 1.0f, suggestions);
}



        // 5. Follow-up questions about the previously selected address
        if (IsBinFollowUpIntent(normMsg) &&
            hasAddress &&
            !string.IsNullOrWhiteSpace(lastBinResult))
        {
            var reply = BuildBinFollowUpReply(normMsg, activeAddress ?? "", lastBinResult ?? "");

            var binFollowUpSuggestions = new List<string>
            {
                "Tell me about general waste",
                "Tell me about recycling",
                "Tell me about garden waste",
                "Use different address"
            };

            SaveConversation(sessionId, message, reply, "Waste & Bins", "bin_result_follow_up", binFollowUpSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, binFollowUpSuggestions);
        }
        //         // 6. Generic waste follow-up only when no more specific address/bin-result intent matched
        // if (IsWasteFollowUpIntent(normMsg) &&
        //     !LooksLikeUkPostcode(message) &&
        //     !IsBinCollectionDayIntent(normMsg) &&
        //     !IsSameAddressIntent(normMsg) &&
        //     !IsDifferentAddressIntent(normMsg) &&
        //     !IsBinFollowUpIntent(normMsg) &&
        //     (!string.IsNullOrWhiteSpace(maskedPostcode) || hasAddress))
        // {
        //     var reply = hasAddress
        //         ? "You have a previously selected address in this chat. Would you like to use the same address, or check a different one?"
        //         : "You were previously asking about a waste query. Would you like to use the same postcode, or check a different one?";

        //     var wasteFollowUpSuggestions = new List<string>
        //     {
        //         hasAddress ? "Use same address" : "Use same postcode",
        //         "Use different address",
        //         "When is my bin collection?",
        //         "Report a missed bin"
        //     };

        //     SaveConversation(sessionId, message, reply, "Waste & Bins", "waste_follow_up", wasteFollowUpSuggestions);
        //     return (reply, "Waste & Bins", "", 1.0f, wasteFollowUpSuggestions);
        // }
        // // 
        // 6. Generic waste follow-up only for address/collection continuation

     if (IsWasteFollowUpIntent(normMsg) &&
           !LooksLikeUkPostcode(message) &&
            !IsNewBinRequestIntent(normMsg) &&
            !normMsg.Contains("cost") &&
            !normMsg.Contains("price") &&
            !normMsg.Contains("how much") &&
            !normMsg.Contains("apply") &&
            !normMsg.Contains("request") &&
        !IsBinCollectionDayIntent(normMsg) &&
        !IsSameAddressIntent(normMsg) &&
        !IsDifferentAddressIntent(normMsg) &&
        !IsBinFollowUpIntent(normMsg) &&
        (!string.IsNullOrWhiteSpace(maskedPostcode) || hasAddress))
    {
    var reply = hasAddress
        ? "You have a previously selected address in this chat. Would you like to use the same address, or check a different one?"
        : "You were previously asking about a waste query. Would you like to use the same postcode, or check a different one?";

    var wasteFollowUpSuggestions = new List<string>
    {
        hasAddress ? "Use same address" : "Use same postcode",
        "Use different address",
        "When is my bin collection?",
        "Report a missed bin"
    };

    SaveConversation(sessionId, message, reply, "Waste & Bins", "waste_follow_up", wasteFollowUpSuggestions);
    return (reply, "Waste & Bins", "", 1.0f, wasteFollowUpSuggestions);
    }
        // 4. Special bin collection flow
        if (normMsg.Contains("sunday") && normMsg.Contains("bin"))
    {
    _memory.SetPendingFlow(sessionId, "awaiting_postcode_for_bin_collection");

    var reply = "Bin collection days depend on your address. Please enter your postcode so I can check your collection schedule.";
    var suggestions = new List<string>
    {
        "BD3 8PX",
        "Report a missed bin"
    };

    SaveConversation(sessionId, message, reply, "Waste & Bins", "bin_collection_lookup", suggestions);
    return (reply, "Waste & Bins", "", 1.0f, suggestions);
    }

        if (IsBinCollectionDayIntent(normMsg))
        {
            _memory.SetPendingFlow(sessionId, "awaiting_postcode_for_bin_collection");

            var reply = "Please enter your postcode so I can look up the address options for your bin collection day.";
            var binCollectionSuggestions = new List<string>
            {
                "Enter postcode",
                "Report a missed bin",
                "Request a new bin"
            };

            SaveConversation(sessionId, message, reply, "Waste & Bins", "bin_collection_lookup", binCollectionSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, binCollectionSuggestions);
        }

        // 5. If user enters a postcode after bin/waste flow, send special frontend signal
        if (LooksLikeUkPostcode(message) &&
            (string.Equals(lastService, "Waste & Bins", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(pendingFlow, "awaiting_postcode_for_bin_collection", StringComparison.OrdinalIgnoreCase)))
        {
            var postcode = message.Trim().ToUpperInvariant();
            _memory.SetMaskedPostcode(sessionId, postcode);
            _memory.SetPendingFlow(sessionId, "postcode_lookup_started");

            var reply = $"POSTCODE_LOOKUP::{postcode}";
            var postcodeSuggestions = new List<string>();

            SaveConversation(sessionId, message, reply, "Waste & Bins", "postcode_lookup", postcodeSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, postcodeSuggestions);
        }

        // 6. Use light service hinting
        var detectedService = DetectService(normMsg, _strongServiceTriggers);

        // 7. If no direct service is detected, lean on last service for short continuation
        if (string.IsNullOrWhiteSpace(detectedService) && !string.IsNullOrWhiteSpace(lastService) && IsLikelyContinuation(normMsg))
        {
            detectedService = lastService;
        }

        // 8. Embed query
        var qEmb = await _embed.EmbedAsync(message);
        var threshold = _config.GetValue("Retrieval:Threshold", 0.45f);

        // 9. Retrieve candidate chunks
        List<(FaqChunk chunk, float score)> top =
            !string.IsNullOrWhiteSpace(detectedService)
                ? _retrieval.TopKInService(qEmb, detectedService, 4)
                : _retrieval.TopK(qEmb, 4);

        var best = top.FirstOrDefault();
        var bestChunk = best.chunk;
        var bestScore = best.score;

        // 10. Build context
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

        // 11. Build service hint
        var serviceHint =
            !string.IsNullOrWhiteSpace(detectedService) ? detectedService :
            !string.IsNullOrWhiteSpace(lastService) ? lastService :
            bestChunk?.Service ?? "Unknown";

        var detectedIntent = DetectIntent(normMsg, serviceHint);

        // 12. If retrieval is weak, still let the agent try first
        if (bestChunk == null || bestScore < threshold)
        {
            var weakContextAgentResult = await _langChain.RunAgentAsync(message, serviceHint, context, history);

            var weakToolHandled = HandleAgentToolResponse(
                sessionId,
                message,
                weakContextAgentResult,
                bestScore);

            if (weakToolHandled.hasToolResponse)
                return weakToolHandled.result;

            var weakReply = weakContextAgentResult.answer;
            var weakService = string.IsNullOrWhiteSpace(weakContextAgentResult.service)
                ? serviceHint
                : weakContextAgentResult.service;
            var weakNextStepsUrl = weakContextAgentResult.nextStepsUrl ?? "";
            var weakSuggestions = BuildSuggestions(weakService, detectedIntent);

            if (!string.IsNullOrWhiteSpace(weakReply) &&
                !weakReply.Contains("I’m not sure", StringComparison.OrdinalIgnoreCase) &&
                !weakReply.Contains("not configured", StringComparison.OrdinalIgnoreCase))
            {
                SaveConversation(sessionId, message, weakReply, weakService, detectedIntent, weakSuggestions);
                return (weakReply, weakService, weakNextStepsUrl, bestScore, weakSuggestions);
            }

            var clarificationReply =
                "I’m not fully sure which council service this is about yet. Can you tell me whether it relates to Council Tax, Bins and Waste, Benefits and Support, or School Admissions?";
            var clarificationSuggestions = new List<string>
            {
                "Council Tax",
                "Waste & Bins",
                "Benefits & Support",
                "School Admissions"
            };

            SaveConversation(sessionId, message, clarificationReply, "Unknown", "clarification", clarificationSuggestions);
            return (clarificationReply, "Unknown", "", bestScore, clarificationSuggestions);
        }

        // 13. Strong retrieved service
        var finalService = string.IsNullOrWhiteSpace(bestChunk.Service) ? "Unknown" : bestChunk.Service;
        _memory.SetLastService(sessionId, finalService);
        _memory.SetLastIntent(sessionId, detectedIntent);

        // 14. Let the LangChain agent decide
        var agentResult = await _langChain.RunAgentAsync(message, finalService, context, history);

        var toolHandled = HandleAgentToolResponse(
            sessionId,
            message,
            agentResult,
            bestScore);

        if (toolHandled.hasToolResponse)
            return toolHandled.result;

        var aiReply = agentResult.answer;
        var resolvedService = string.IsNullOrWhiteSpace(agentResult.service) ? finalService : agentResult.service;
        var resolvedNextStepsUrl = string.IsNullOrWhiteSpace(agentResult.nextStepsUrl)
            ? (bestChunk.NextStepsUrl ?? "")
            : agentResult.nextStepsUrl;

        // 15. Fallback to direct OpenAI
        if (string.IsNullOrWhiteSpace(aiReply))
        {
            aiReply = await _openAi.GenerateAnswerAsync(message, finalService, context);
        }

        // 16. Final fallback
        if (string.IsNullOrWhiteSpace(aiReply))
        {
            aiReply = bestChunk.Text ?? "Sorry — I could not find a reliable answer from the available council information.";
        }

        var finalSuggestions = BuildSuggestions(resolvedService, detectedIntent);

        SaveConversation(sessionId, message, aiReply, resolvedService, detectedIntent, finalSuggestions);

        return (aiReply, resolvedService, resolvedNextStepsUrl, bestScore, finalSuggestions);
    }

    private (bool hasToolResponse, (string reply, string service, string nextStepsUrl, float score, List<string> suggestions) result)
        HandleAgentToolResponse(
            string sessionId,
            string userMessage,
            (string answer, string service, string action, bool needsClarification, string toolUsed, string nextStepsUrl) agentResult,
            float score)
    {
        if (!string.Equals(agentResult.action, "tool", StringComparison.OrdinalIgnoreCase))
            return (false, default);

        if (string.Equals(agentResult.toolUsed, "postcode_lookup", StringComparison.OrdinalIgnoreCase))
        {
            var postcode = agentResult.answer
                .Replace("POSTCODE_LOOKUP::", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            _memory.SetMaskedPostcode(sessionId, postcode);
            _memory.SetPendingFlow(sessionId, "postcode_lookup_started");

            var reply = $"POSTCODE_LOOKUP::{postcode}";
            var service = string.IsNullOrWhiteSpace(agentResult.service) ? "Waste & Bins" : agentResult.service;
            var toolSuggestions = new List<string>();

            SaveConversation(sessionId, userMessage, reply, service, "postcode_lookup", toolSuggestions);
            return (true, (reply, service, "", score, toolSuggestions));
        }

        if (!string.IsNullOrWhiteSpace(agentResult.answer))
        {
            var service = string.IsNullOrWhiteSpace(agentResult.service) ? "Unknown" : agentResult.service;
            var toolSuggestions = BuildSuggestions(service, _memory.GetLastIntent(sessionId));

            SaveConversation(sessionId, userMessage, agentResult.answer, service, _memory.GetLastIntent(sessionId), toolSuggestions);
            return (true, (agentResult.answer, service, agentResult.nextStepsUrl ?? "", score, toolSuggestions));
        }

        return (false, default);
    }

    private void SaveConversation(string sessionId, string userMessage, string assistantReply, string service, string intent, List<string> suggestions)
    {
        _memory.AddTurn(sessionId, "user", userMessage);
        _memory.AddTurn(sessionId, "assistant", assistantReply);
        _memory.SetLastService(sessionId, service);
        _memory.SetLastIntent(sessionId, intent);
        _memory.SetLastSuggestions(sessionId, suggestions);
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        input = input.ToLowerInvariant();
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

    private static string DetectIntent(string normMsg, string service)
{
    if (string.IsNullOrWhiteSpace(normMsg))
        return "";

    if (normMsg.Contains("apply"))
        return "apply";

    if (normMsg.Contains("eligible") || normMsg.Contains("eligibility") || normMsg.Contains("qualify"))
        return "eligibility";

    if (normMsg.Contains("pay") || normMsg.Contains("payment") || normMsg.Contains("balance"))
        return "payment";

    if (normMsg.Contains("missed bin"))
        return "missed_bin";

    if (normMsg.Contains("new bin") || normMsg.Contains("replacement bin"))
        return "new_bin";

    if (normMsg.Contains("collection"))
        return "collection";

    if (normMsg.Contains("planning application") || normMsg.Contains("planning permission"))
        return "planning";

    if (normMsg.Contains("library") || normMsg.Contains("renew books") || normMsg.Contains("e-books"))
        return "library";

    if (normMsg.Contains("housing") || normMsg.Contains("homeless"))
        return "housing";

    if (normMsg.Contains("contact") || normMsg.Contains("phone number") || normMsg.Contains("email"))
        return "contact";

    return service?.ToLowerInvariant() ?? "";
}

    private static bool IsBinCollectionDayIntent(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg))
            return false;

        return normMsg.Contains("bin collection day") ||
               normMsg.Contains("bin day") ||
               normMsg.Contains("collection day") ||
               normMsg.Contains("waste collection") ||
               normMsg.Contains("recycling collection") ||
               (normMsg.Contains("bin") && normMsg.Contains("day")) ||
               (normMsg.Contains("recycling") && normMsg.Contains("day"));
    }

    // private static bool IsWasteFollowUpIntent(string normMsg)
    // {
    //     if (string.IsNullOrWhiteSpace(normMsg))
    //         return false;

    //     return normMsg.Contains("bin") ||
    //            normMsg.Contains("bins") ||
    //            normMsg.Contains("waste") ||
    //            normMsg.Contains("recycling") ||
    //            normMsg.Contains("address") ||
    //            normMsg.Contains("same address") ||
    //            normMsg.Contains("different address");
    // }
    private static bool IsWasteFollowUpIntent(string normMsg)
{
    if (string.IsNullOrWhiteSpace(normMsg))
        return false;

    return normMsg.Contains("same address") ||
           normMsg.Contains("different address") ||
           normMsg.Contains("same postcode") ||
           normMsg.Contains("previous address") ||
           normMsg.Contains("address i told you before") ||
           normMsg.Contains("previously selected address") ||
           normMsg.Contains("previous postcode");
}

    private static bool IsShortFollowUp(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg))
            return false;

        return normMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5;
    }

    private static bool IsLikelyContinuation(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg))
            return false;

        return IsShortFollowUp(normMsg) ||
               normMsg.Contains("how do i apply") ||
               normMsg.Contains("contact details") ||
               normMsg.Contains("what do i need") ||
               normMsg.Contains("am i eligible") ||
               normMsg.Contains("how much") ||
               normMsg.Contains("what next");
    }

    private static bool LooksLikeUkPostcode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var cleaned = input.Trim().ToUpperInvariant();
        return Regex.IsMatch(cleaned, @"^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$");
    }

    private static List<string> BuildSuggestions(string service, string intent)
    {
        var svc = service?.Trim() ?? "";
        var suggestions = new List<string>();

        switch (svc)
        {
            case "Council Tax":
                suggestions.Add("How do I pay my Council Tax?");
                suggestions.Add("Can I get a Council Tax discount?");
                suggestions.Add("I have moved home");
                break;

            case "Waste & Bins":
                suggestions.Add("When is my bin collection?");
                suggestions.Add("Report a missed bin");
                suggestions.Add("Request a new bin");
                break;

            case "Benefits & Support":
                suggestions.Add("How do I apply?");
                suggestions.Add("Am I eligible?");
                suggestions.Add("What evidence do I need?");
                break;

            case "Education":
                suggestions.Add("How do I apply for a school place?");
                suggestions.Add("What is the deadline?");
                suggestions.Add("How do in-year transfers work?");
                break;

            
            case "Planning":
                suggestions.Add("How can I check my planning application status?");
                suggestions.Add("View planning applications");
                suggestions.Add("How do I apply for planning permission?");
                break;

            case "Libraries":
                suggestions.Add("How do I renew library books online?");
                suggestions.Add("Can I borrow e-books?");
                suggestions.Add("How do I join the library?");
                break;

            case "Housing":
                suggestions.Add("How do I get housing support?");
                suggestions.Add("I am homeless");
                suggestions.Add("How can I find a home?");
                break;

            case "Contact Us":
                suggestions.Add("How can I contact the council?");
                suggestions.Add("What is the council phone number?");
                suggestions.Add("How do I sign up for email alerts?");
                break;
            default:
                suggestions.Add("Council Tax");
                suggestions.Add("Waste & Bins");
                suggestions.Add("Benefits & Support");
                suggestions.Add("Planning");
                break;
        }

        if (svc == "Waste & Bins" && intent == "collection")
        {
            suggestions.Insert(0, "Use same address");
            suggestions.Insert(1, "Use different address");
        }

        return suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
    }
    private static bool IsSameAddressIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("same address");
}

private static bool IsDifferentAddressIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("different address");
}
    private static bool IsBinFollowUpIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("general waste") ||
           msg.Contains("recycling") ||
           msg.Contains("garden waste") ||
           msg.Contains("what about garden waste") ||
           msg.Contains("what about recycling") ||
           msg.Contains("what about general waste") ||
           msg.Contains("tell me about the bin collection") ||
           msg.Contains("bin collection of the address i told you before") ||
           msg.Contains("address i told you before") ||
           msg.Contains("previously selected address");
}

    private static string BuildBinFollowUpReply(string normMsg, string activeAddress, string lastBinResult)
    {
        if (string.IsNullOrWhiteSpace(lastBinResult))
            return "I could not find the previous bin collection details for that address.";

        if (normMsg.Contains("general waste"))
        {
            var section = ExtractSection(lastBinResult, "General waste:");
            return string.IsNullOrWhiteSpace(section)
                ? $"For your previously selected address, here are the bin collection details:\n\n{lastBinResult}"
                : $"For your previously selected address, the general waste collection details are:\n\n{section}";
        }

        if (normMsg.Contains("recycling"))
        {
            var section = ExtractSection(lastBinResult, "Recycling waste:");
            return string.IsNullOrWhiteSpace(section)
                ? $"For your previously selected address, here are the bin collection details:\n\n{lastBinResult}"
                : $"For your previously selected address, the recycling collection details are:\n\n{section}";
        }

        if (normMsg.Contains("garden waste"))
        {
            var section = ExtractGardenWasteSection(lastBinResult);
            return string.IsNullOrWhiteSpace(section)
                ? $"For your previously selected address, here are the bin collection details:\n\n{lastBinResult}"
                : $"For your previously selected address, the garden waste details are:\n\n{section}";
        }

        return $"For your previously selected address, here are the bin collection details:\n\n{lastBinResult}";
    }

    private static string ExtractSection(string text, string heading)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToList();

        var start = lines.FindIndex(x => x.StartsWith(heading, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
            return "";

        var collected = new List<string> { lines[start] };

        for (int i = start + 1; i < lines.Count; i++)
        {
            var line = lines[i];

            if (!line.StartsWith("-") &&
                line.EndsWith(":") &&
                !line.StartsWith(heading, StringComparison.OrdinalIgnoreCase))
                break;

            if (line.StartsWith("Garden waste subscription", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Garden waste:", StringComparison.OrdinalIgnoreCase))
                break;

            collected.Add(line);
        }

        return string.Join("\n", collected);
    }

    private static string ExtractGardenWasteSection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToList();

        var collected = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("Garden waste subscription", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Garden waste:", StringComparison.OrdinalIgnoreCase))
            {
                collected.Add(line);
            }
        }

        return string.Join("\n", collected);
    }

    private static bool IsContextResetIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("something else") ||
           msg.Contains("ask something else") ||
           msg.Contains("different question") ||
           msg.Contains("another question") ||
           msg.Contains("different topic");
}

private static bool IsMissedBinIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("missed bin") ||
           msg.Contains("bin was not collected") ||
           msg.Contains("my bin was not collected") ||
           msg.Contains("report a missed bin") ||
           msg.Contains("missed collection");
}
    private static bool IsNewBinRequestIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("new bin") ||
           msg.Contains("replacement bin") ||
           msg.Contains("bin cost") ||
           msg.Contains("cost of a new bin") ||
           msg.Contains("how much does the new bin cost") ||
           msg.Contains("how much does a new bin cost") ||
           msg.Contains("request a new bin") ||
           msg.Contains("get new wheeled bins") ||
           msg.Contains("recycling containers") ||
           msg.Contains("new recycle bin") ||
           msg.Contains("new recycling bin") ||
           msg.Contains("replacement recycling container") ||
           msg.Contains("replacement container");
}
private static bool IsEnterPostcodeIntent(string normMsg)
{
    return normMsg == "enter postcode" || normMsg == "postcode";
}
}