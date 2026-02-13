using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class ChatScoringService
{
    // --- Service synonyms / intent hints (edit freely) ---
    public Dictionary<string, string[]> ServiceSynonyms { get; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Council Tax"] = new[] { "council tax", "ctax", "bill", "balance", "arrears", "direct debit", "discount", "exemption", "move home" },
            ["Waste & Bins"] = new[] { "bin", "bins", "waste", "recycling", "missed", "collection", "bulky", "replacement bin" },
            ["Benefits & Support"] = new[] { "benefit", "benefits", "support", "council tax support", "housing benefit", "universal credit", "hardship", "low income" },
            ["Education"] = new[] { "school", "admissions", "apply", "deadline", "in-year", "transfer", "send", "ehcp", "transport" }
        };

    public string DetectService(string message)
    {
        var msg = Normalize(message);

        string bestService = "";
        int bestHits = 0;

        foreach (var kv in ServiceSynonyms)
        {
            int hits = 0;
            foreach (var syn in kv.Value)
            {
                var s = Normalize(syn);
                if (!string.IsNullOrWhiteSpace(s) && msg.Contains(s))
                    hits++;
            }

            if (hits > bestHits)
            {
                bestHits = hits;
                bestService = kv.Key;
            }
        }

        return bestHits > 0 ? bestService : "";
    }

    public int Score(string message, FaqItem faq)
    {
        var msg = Normalize(message);
        int score = 0;

        // 1) Title boosts
        if (!string.IsNullOrWhiteSpace(faq.Title))
        {
            var title = Normalize(faq.Title);
            if (msg.Contains(title)) score += 10;

            foreach (var w in Tokenize(title))
                if (w.Length >= 4 && msg.Contains(w)) score += 2;
        }

        // 2) Keyword boosts
        if (faq.Keywords != null)
        {
            foreach (var k in faq.Keywords)
            {
                var kw = Normalize(k);
                if (string.IsNullOrWhiteSpace(kw)) continue;

                if (msg.Contains(kw))
                    score += kw.Length >= 10 ? 6 : 4;
                else
                    foreach (var t in Tokenize(kw))
                        if (t.Length >= 4 && msg.Contains(t)) score += 1;
            }
        }

        // 3) Service boost if user mentions it
        if (!string.IsNullOrWhiteSpace(faq.Service))
        {
            var svc = Normalize(faq.Service);
            if (!string.IsNullOrWhiteSpace(svc) && msg.Contains(svc)) score += 2;
        }

        // 4) Synonym boost for the FAQ service
        if (!string.IsNullOrWhiteSpace(faq.Service) && ServiceSynonyms.TryGetValue(faq.Service, out var syns))
        {
            foreach (var syn in syns)
            {
                var s = Normalize(syn);
                if (!string.IsNullOrWhiteSpace(s) && msg.Contains(s)) score += 1;
            }
        }

        return score;
    }

    public string PickReply(FaqItem faq)
    {
        if (faq.Responses != null && faq.Responses.Count > 0)
            return faq.Responses[Random.Shared.Next(faq.Responses.Count)];

        return faq.Answer ?? "";
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        input = input.ToLowerInvariant();
        var chars = input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static List<string> Tokenize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new List<string>();
        return input.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}