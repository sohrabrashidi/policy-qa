using PolicyQa.Core.Retrieval;

namespace PolicyQa.Core.Llm;

/// <summary>
/// Offline stand-in for a real embedding model: feature-hashed unigrams and
/// bigrams. Much weaker than a neural model, but deterministic and free, which
/// makes it right for tests and for trying the project without an API key.
/// </summary>
public sealed class HashingEmbedder(int dimensions = 1024) : IEmbeddingClient
{
    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<float[]>>(inputs.Select(Embed).ToList());

    private float[] Embed(string text)
    {
        var vector = new float[dimensions];
        var tokens = Tokenizer.Tokens(text).ToList();

        for (var i = 0; i < tokens.Count; i++)
        {
            Add(vector, tokens[i], 1f);
            if (i > 0)
            {
                Add(vector, $"{tokens[i - 1]}_{tokens[i]}", 0.5f);
            }
        }

        return vector;
    }

    private void Add(float[] vector, string feature, float weight)
    {
        var hash = Fnv1a(feature);
        var index = (int)(hash % (uint)dimensions);
        // Use one bit of the hash as a sign to reduce collision bias.
        vector[index] += (hash & 0x80000000) == 0 ? weight : -weight;
    }

    // FNV-1a: stable across processes and runtimes, unlike string.GetHashCode().
    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash;
    }
}
