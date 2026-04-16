using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public static class ChunkingService
{
    // Simple chunking: split answer into ~2-3 sentence chunks
    public static List<FaqChunk> BuildChunks(List<FaqItem> faqs, int maxChars = 420)
    {
        var chunks = new List<FaqChunk>();
        int id = 0;

        foreach (var faq in faqs)
        {
            var parts = SplitIntoSentences(faq.Answer);
            var current = "";

            foreach (var s in parts)
            {
                if ((current + " " + s).Trim().Length > maxChars && current.Length > 0)
                {
                    chunks.Add(ToChunk(faq, ++id, current.Trim()));
                    current = s;
                }
                else
                {
                    current = (current + " " + s).Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(current))
                chunks.Add(ToChunk(faq, ++id, current.Trim()));
        }

        return chunks;
    }

    private static FaqChunk ToChunk(FaqItem faq, int id, string text) => new()
    {
        Id = $"c{id}",
        Service = faq.Service ?? "",
        Title = faq.Title ?? "",
        Text = text,
        NextStepsUrl = faq.NextStepsUrl ?? ""
    };

    private static List<string> SplitIntoSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();

        // naive sentence split (good enough for prototype)
        var seps = new[] { ". ", "? ", "! " };
        var temp = text.Replace("\r", " ").Replace("\n", " ").Trim();

        var sentences = new List<string>();
        int start = 0;

        for (int i = 0; i < temp.Length - 1; i++)
        {
            if (temp[i] == '.' || temp[i] == '?' || temp[i] == '!')
            {
                // end sentence at punctuation
                var s = temp.Substring(start, i - start + 1).Trim();
                if (s.Length > 0) sentences.Add(s);
                start = i + 1;
            }
        }

        // tail
        var tail = temp.Substring(start).Trim();
        if (tail.Length > 0) sentences.Add(tail);

        // merge tiny sentences
        var merged = new List<string>();
        foreach (var s in sentences)
        {
            if (merged.Count == 0) merged.Add(s);
            else
            {
                if (s.Length < 30) merged[^1] = (merged[^1] + " " + s).Trim();
                else merged.Add(s);
            }
        }

        return merged;
    }
}