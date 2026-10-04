using PolicyQa.Core.Ingestion;
using PolicyQa.Core.Llm;

namespace PolicyQa.Core.Retrieval;

/// <summary>
/// Runs keyword (BM25) and vector search, then merges the two rankings with
/// Reciprocal Rank Fusion. RRF only looks at positions, so there is no need to
/// calibrate BM25 scores against cosine similarities.
/// </summary>
public sealed class HybridRetriever(IEmbeddingClient embeddings)
{
    private const int RrfK = 60;

    private readonly Bm25Index _keyword = new();
    private readonly VectorIndex _vectors = new();
    private readonly Dictionary<string, SourceDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public IReadOnlyCollection<SourceDocument> Documents => _documents.Values;

    public int ChunkCount => _keyword.Count;

    public async Task<int> AddAsync(SourceDocument document, IReadOnlyList<Chunk> chunks, CancellationToken ct)
    {
        if (chunks.Count == 0)
        {
            return 0;
        }

        var vectors = await embeddings.EmbedAsync(chunks.Select(c => $"{c.DocumentTitle}. {c.Section}. {c.Text}").ToList(), ct);

        await _lock.WaitAsync(ct);
        try
        {
            if (!_documents.TryAdd(document.Id, document))
            {
                throw new InvalidOperationException($"Document '{document.Id}' is already indexed.");
            }

            for (var i = 0; i < chunks.Count; i++)
            {
                _keyword.Add(chunks[i]);
                _vectors.Add(chunks[i], vectors[i]);
            }
        }
        finally
        {
            _lock.Release();
        }

        return chunks.Count;
    }

    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(string question, int top, CancellationToken ct)
    {
        var queryVector = (await embeddings.EmbedAsync([question], ct))[0];
        var pool = Math.Max(top * 4, 20);

        await _lock.WaitAsync(ct);
        try
        {
            var keyword = _keyword.Search(question, pool);
            var semantic = _vectors.Search(queryVector, pool);

            var fused = new Dictionary<string, (Chunk Chunk, double Score)>();
            Accumulate(keyword);
            Accumulate(semantic);

            return fused.Values
                .OrderByDescending(f => f.Score)
                .Take(top)
                .Select(f => new ScoredChunk(f.Chunk, f.Score))
                .ToList();

            void Accumulate(IReadOnlyList<ScoredChunk> ranking)
            {
                for (var rank = 0; rank < ranking.Count; rank++)
                {
                    var chunk = ranking[rank].Chunk;
                    var add = 1.0 / (RrfK + rank + 1);
                    fused[chunk.Id] = fused.TryGetValue(chunk.Id, out var existing)
                        ? (chunk, existing.Score + add)
                        : (chunk, add);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
