namespace PolicyQa.Core.Llm;

public interface IEmbeddingClient
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct);
}

public interface IChatModel
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}
