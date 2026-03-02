using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class RetrievalService
{
    private readonly IReadOnlyList<FaqChunk> _chunks;

    public RetrievalService(IReadOnlyList<FaqChunk> chunks)
    {
        _chunks = chunks;
    }

    public List<(FaqChunk chunk, float score)> TopK(float[] queryEmbedding, int k = 3)
    {
        var scored = new List<(FaqChunk chunk, float score)>();

        foreach (var c in _chunks)
        {
            if (c.Vector == null || c.Vector.Length == 0) continue;
            var sim = VectorStore.Cosine(queryEmbedding, c.Vector);
            scored.Add((c, sim));
        }

        return scored
            .OrderByDescending(x => x.score)
            .Take(k)
            .ToList();
    }

    public List<(FaqChunk chunk, float score)> TopKInService(float[] queryEmbedding, string service, int k = 3)
    {
        var scored = new List<(FaqChunk chunk, float score)>();

        foreach (var c in _chunks)
        {
            if (!string.Equals(c.Service, service, StringComparison.OrdinalIgnoreCase))
                continue;

            if (c.Vector == null || c.Vector.Length == 0) continue;
            var sim = VectorStore.Cosine(queryEmbedding, c.Vector);
            scored.Add((c, sim));
        }

        return scored
            .OrderByDescending(x => x.score)
            .Take(k)
            .ToList();
    }
}