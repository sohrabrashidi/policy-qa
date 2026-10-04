using PolicyQa.Core.Ingestion;

namespace PolicyQa.Core.Retrieval;

/// <summary>
/// Classic BM25 keyword scoring. Embeddings are good at meaning, but compliance
/// questions often hinge on exact terms ("PEP", "CTR", "5 years") where keyword
/// search is more reliable. The two are combined in <see cref="HybridRetriever"/>.
/// </summary>
public sealed class Bm25Index
{
    private const double K1 = 1.4;
    private const double B = 0.75;

    private readonly List<(Chunk Chunk, Dictionary<string, int> Terms, int Length)> _docs = [];
    private readonly Dictionary<string, int> _documentFrequency = new(StringComparer.Ordinal);

    public int Count => _docs.Count;

    public void Add(Chunk chunk)
    {
        var terms = new Dictionary<string, int>(StringComparer.Ordinal);
        var length = 0;
        foreach (var token in Tokenizer.Tokens($"{chunk.Section} {chunk.Text}"))
        {
            terms[token] = terms.GetValueOrDefault(token) + 1;
            length++;
        }

        foreach (var term in terms.Keys)
        {
            _documentFrequency[term] = _documentFrequency.GetValueOrDefault(term) + 1;
        }

        _docs.Add((chunk, terms, length));
    }

    public IReadOnlyList<ScoredChunk> Search(string query, int top)
    {
        if (_docs.Count == 0)
        {
            return [];
        }

        var queryTerms = Tokenizer.Tokens(query).Distinct().ToList();
        var averageLength = _docs.Average(d => d.Length);
        var n = _docs.Count;

        return _docs
            .Select(d =>
            {
                double score = 0;
                foreach (var term in queryTerms)
                {
                    if (!d.Terms.TryGetValue(term, out var tf))
                    {
                        continue;
                    }

                    var df = _documentFrequency[term];
                    var idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5));
                    score += idf * tf * (K1 + 1) / (tf + K1 * (1 - B + B * d.Length / averageLength));
                }

                return new ScoredChunk(d.Chunk, score);
            })
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .Take(top)
            .ToList();
    }
}

public sealed record ScoredChunk(Chunk Chunk, double Score);
