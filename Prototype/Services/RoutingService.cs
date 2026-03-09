using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class RoutingService
{
    private readonly SupportResourceRepository _supportResources;

    private readonly string[] _crisisTriggers =
    {
        "suicide", "kill myself", "end my life", "want to die",
        "self harm", "self-harm", "hurt myself", "commit suicide"
    };

    private readonly string[] _mentalHealthTriggers =
    {
        "mental health", "depression", "anxiety", "struggling mentally",
        "feeling depressed", "panic attack", "panic attacks",
        "mental wellbeing", "mental support", "dealing with depression"
    };

    private readonly Dictionary<string, string[]> _faqTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Council Tax"] = new[]
        {
            "council tax", "ctax", "bill", "balance", "arrears", "discount",
            "exemption", "direct debit", "pay", "payment", "instalment", "installment",
            "move home", "moved house", "moved"
        },
        ["Waste & Bins"] = new[]
        {
            "bin", "bins", "waste", "recycling", "collection", "missed bin",
            "replacement bin", "bin day", "bulky waste", "bulky"
        },
        ["Benefits & Support"] = new[]
        {
            "benefit", "benefits", "housing benefit", "universal credit",
            "council tax support", "hardship", "financial support", "change of circumstances"
        },
        ["Education"] = new[]
        {
            "school", "admission", "admissions", "school place", "deadline",
            "in-year", "transfer", "send", "ehcp", "transport"
        }
    };

    public RoutingService(SupportResourceRepository supportResources)
    {
        _supportResources = supportResources;
    }

    public RouteDecision Classify(string message, string currentMode = "", string lastService = "")
    {
        var norm = Normalize(message);

        if (ContainsAny(norm, _crisisTriggers))
        {
            return new RouteDecision
            {
                Mode = "crisis",
                Service = "Crisis Support",
                Intent = "crisis_support"
            };
        }

        if (ContainsAny(norm, _mentalHealthTriggers))
        {
            var resource = _supportResources.FindBestMatch(message);
            return new RouteDecision
            {
                Mode = "signpost",
                Service = "Mental Health Support",
                Intent = "mental_health_support",
                NextStepsUrl = resource?.Url ?? ""
            };
        }

        var detectedService = DetectFaqService(norm);
        if (!string.IsNullOrWhiteSpace(detectedService))
        {
            return new RouteDecision
            {
                Mode = "faq",
                Service = detectedService,
                Intent = "faq_query"
            };
        }

        if (LooksLikeGeneralHelp(norm))
        {
            return new RouteDecision
            {
                Mode = "out_of_scope",
                Service = "Unknown",
                Intent = "general_help"
            };
        }

        return new RouteDecision
        {
            Mode = "out_of_scope",
            Service = "Unknown",
            Intent = "unknown"
        };
    }

    private bool LooksLikeGeneralHelp(string norm)
    {
        return norm.Contains("resources")
            || norm.Contains("support")
            || norm.Contains("help")
            || norm.Contains("services")
            || norm.Contains("in bradford");
    }

    private string DetectFaqService(string norm)
    {
        foreach (var kv in _faqTriggers)
        {
            foreach (var t in kv.Value)
            {
                var tt = Normalize(t);
                if (!string.IsNullOrWhiteSpace(tt) && norm.Contains(tt))
                    return kv.Key;
            }
        }

        return "";
    }

    private static bool ContainsAny(string norm, IEnumerable<string> triggers)
    {
        foreach (var t in triggers)
        {
            var tt = Normalize(t);
            if (!string.IsNullOrWhiteSpace(tt) && norm.Contains(tt))
                return true;
        }
        return false;
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        input = input.ToLowerInvariant();
        var chars = input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}