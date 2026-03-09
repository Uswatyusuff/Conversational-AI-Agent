using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class SupportResourceRepository
{
    private readonly List<SupportResource> _resources = new()
    {
        new SupportResource
        {
            Category = "mental_health",
            Title = "Bradford District Care NHS Foundation Trust mental health services",
            Description = "Non-urgent NHS mental health services and support in Bradford.",
            Url = "https://www.bdct.nhs.uk/our-services/mental-health-services/",
            Keywords = new List<string>
            {
                "mental health", "depression", "anxiety", "struggling mentally",
                "mental wellbeing", "panic attacks", "emotional support", "dealing with depression"
            }
        }
    };

    public List<SupportResource> GetAll() => _resources;

    public SupportResource? FindBestMatch(string message)
    {
        var norm = Normalize(message);
        int bestScore = 0;
        SupportResource? best = null;

        foreach (var r in _resources)
        {
            int score = 0;

            if (norm.Contains(Normalize(r.Category)))
                score += 2;

            foreach (var kw in r.Keywords)
            {
                var k = Normalize(kw);
                if (!string.IsNullOrWhiteSpace(k) && norm.Contains(k))
                    score += 3;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = r;
            }
        }

        return best;
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        input = input.ToLowerInvariant();
        var chars = input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}