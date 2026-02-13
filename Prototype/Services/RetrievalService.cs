using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class RetrievalService
{
    private readonly List<(FaqItem faq, float[] embedding)> _items;

    public RetrievalService(List<(FaqItem faq, float[] embedding)> items)
    {
        _items = items;
    }

    public List<(FaqItem faq, float score)> TopK(float[] queryEmbedding, int k = 3)
    {
        var scored = new List<(FaqItem faq, float score)>();

        foreach (var (faq, emb) in _items)
        {
            var sim = VectorStore.Cosine(queryEmbedding, emb);
            scored.Add((faq, sim));
        }

        return scored
            .OrderByDescending(x => x.score)
            .Take(k)
            .ToList();
    }

    public (FaqItem? faq, float score) BestMatch(float[] queryEmbedding)
    {
        var top = TopK(queryEmbedding, 1).FirstOrDefault();
        return (top.faq, top.score);
    }
}