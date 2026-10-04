using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PolicyQa.Core.Llm;

public sealed class OpenAiOptions
{
    /// <summary>Any OpenAI-compatible endpoint: OpenAI, Azure OpenAI (v1 API), Ollama, LM Studio, vLLM...</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    public string? ApiKey { get; set; }

    public string ChatModel { get; set; } = "gpt-4o-mini";

    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Thin client for the /embeddings and /chat/completions endpoints. Written
/// against HttpClient directly so it works with any compatible server and
/// adds no SDK dependency.
/// </summary>
public sealed class OpenAiCompatibleClient : IEmbeddingClient, IChatModel
{
    private const int EmbeddingBatchSize = 64;

    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;

    public OpenAiCompatibleClient(HttpClient http, OpenAiOptions options)
    {
        _http = http;
        _options = options;
        _http.BaseAddress ??= new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
        _http.Timeout = TimeSpan.FromSeconds(60);
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var result = new List<float[]>(inputs.Count);
        foreach (var batch in inputs.Chunk(EmbeddingBatchSize))
        {
            using var response = await _http.PostAsJsonAsync(
                "embeddings",
                new EmbeddingRequest(_options.EmbeddingModel, batch),
                ct);
            await EnsureSuccess(response, ct);

            var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct)
                ?? throw new InvalidOperationException("Empty embeddings response.");
            result.AddRange(body.Data.OrderBy(d => d.Index).Select(d => d.Embedding));
        }

        return result;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var request = new ChatRequest(
            _options.ChatModel,
            [new ChatMessage("system", systemPrompt), new ChatMessage("user", userPrompt)],
            Temperature: 0);

        using var response = await _http.PostAsJsonAsync("chat/completions", request, ct);
        await EnsureSuccess(response, ct);

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(ct);
        return body?.Choices.FirstOrDefault()?.Message.Content?.Trim()
            ?? throw new InvalidOperationException("Model returned no content.");
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"LLM endpoint returned {(int)response.StatusCode}: {detail[..Math.Min(detail.Length, 500)]}",
            null,
            response.StatusCode);
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IEnumerable<string> Input);

    private sealed record EmbeddingResponse([property: JsonPropertyName("data")] List<EmbeddingItem> Data);

    private sealed record EmbeddingItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content);

    private sealed record ChatResponse([property: JsonPropertyName("choices")] List<ChatChoice> Choices);

    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatMessage Message);
}
