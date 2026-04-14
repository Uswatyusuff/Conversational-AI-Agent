namespace CouncilChatbotPrototype.Services;

public static class VectorStore
{
    public static float Cosine(float[] a, float[] b)
    {
        if (a == null || b == null) return 0f;
        if (a.Length == 0 || b.Length == 0) return 0f;
        if (a.Length != b.Length) return 0f;

        float dot = 0f;
        float normA = 0f;
        float normB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
        if (denom == 0f) return 0f;

        return dot / denom;
    }
}