namespace CouncilChatbotPrototype.Services;

public class IntentService
{
    private static readonly Dictionary<string, string[]> ServiceHints = new()
    {
        ["Council Tax"] = new[] { "council tax", "bill", "discount", "exemption", "direct debit", "arrears", "moved", "move home" },
        ["Waste & Bins"] = new[] { "bin", "bins", "recycling", "missed", "collection", "bulky", "waste" },
        ["Benefits & Support"] = new[] { "benefit", "benefits", "support", "hardship", "housing benefit", "council tax support", "universal credit" },
        ["Education"] = new[] { "school", "admissions", "primary", "secondary", "in-year", "transfer", "send", "ehcp", "transport" }
    };

    public string DetectService(string message)
    {
        var msg = message.ToLowerInvariant();

        foreach (var kvp in ServiceHints)
        {
            if (kvp.Value.Any(h => msg.Contains(h)))
                return kvp.Key;
        }

        return "Unknown";
    }
}