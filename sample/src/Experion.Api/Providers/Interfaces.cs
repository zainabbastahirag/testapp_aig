namespace Experion.Api.Providers;

public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}

public interface ILlmProvider
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

public static class VectorMath
{
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na  += a[i] * a[i];
            nb  += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
