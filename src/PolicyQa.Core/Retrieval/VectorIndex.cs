using PolicyQa.Core.Ingestion;

namespace PolicyQa.Core.Retrieval;

/// <summary>
/// Brute-force cosine similarity over normalised vectors. For a few thousand
/// policy chunks this is fast enough and needs no extra infrastructure;
/// pgvector or a hosted vector DB only pays off well beyond that.
/// </summary>
public sealed class VectorIndex
{
    private readonly List<(Chunk Chunk, float[] Vector)> _items = [];

    public int Count => _items.Count;

    public IEnumerable<(Chunk Chunk, float[] Vector)> Items => _items;

    public void Add(Chunk chunk, float[] vector) => _items.Add((chunk, Normalise(vector)));

    public IReadOnlyList<ScoredChunk> Search(float[] query, int top)
    {
        var q = Normalise(query);
        return _items
            .Select(i => new ScoredChunk(i.Chunk, Dot(q, i.Vector)))
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .Take(top)
            .ToList();
    }

    private static double Dot(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new InvalidOperationException(
                $"Embedding size mismatch ({a.Length} vs {b.Length}). Was the index built with a different model?");
        }

        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    private static float[] Normalise(float[] v)
    {
        double norm = 0;
        foreach (var x in v)
        {
            norm += x * x;
        }

        norm = Math.Sqrt(norm);
        return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
    }
}
