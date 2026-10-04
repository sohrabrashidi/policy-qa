using PolicyQa.Api;
using PolicyQa.Core.Answering;
using PolicyQa.Core.Ingestion;
using PolicyQa.Core.Llm;
using PolicyQa.Core.Retrieval;

var builder = WebApplication.CreateBuilder(args);

var llm = builder.Configuration.GetSection("Llm").Get<OpenAiOptions>() ?? new OpenAiOptions();
builder.Services.AddSingleton(llm);

if (llm.IsConfigured)
{
    builder.Services.AddHttpClient<OpenAiCompatibleClient>();
    builder.Services.AddSingleton<IEmbeddingClient>(sp => sp.GetRequiredService<OpenAiCompatibleClient>());
    builder.Services.AddSingleton<IChatModel>(sp => sp.GetRequiredService<OpenAiCompatibleClient>());
}
else
{
    // No key: offline mode with local embeddings and extractive answers.
    builder.Services.AddSingleton<IEmbeddingClient>(new HashingEmbedder());
}

builder.Services.AddSingleton<MarkdownChunker>();
builder.Services.AddSingleton<HybridRetriever>();
builder.Services.AddSingleton(sp => new AnswerService(
    sp.GetRequiredService<HybridRetriever>(),
    sp.GetService<IChatModel>()));
builder.Services.AddHostedService<StartupIngestion>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", (OpenAiOptions options, HybridRetriever index) => new
{
    service = "policy-qa",
    mode = options.IsConfigured ? $"llm ({options.ChatModel})" : "offline (extractive)",
    documents = index.Documents.Count,
    chunks = index.ChunkCount,
});

app.MapGet("/documents", (HybridRetriever index) =>
    index.Documents.Select(d => new { d.Id, d.Title, characters = d.Content.Length }));

app.MapPost("/documents", async (IFormFile file, MarkdownChunker chunker, HybridRetriever index, CancellationToken ct) =>
    {
        if (file.Length == 0 || file.Length > 2_000_000)
        {
            return Results.Problem(statusCode: 400, title: "File must be between 1 byte and 2 MB.");
        }

        using var reader = new StreamReader(file.OpenReadStream());
        SourceDocument document;
        try
        {
            document = DocumentLoader.FromText(file.FileName, await reader.ReadToEndAsync(ct));
        }
        catch (NotSupportedException ex)
        {
            return Results.Problem(statusCode: 415, title: ex.Message);
        }

        try
        {
            var chunks = await index.AddAsync(document, chunker.Split(document), ct);
            return Results.Created($"/documents/{document.Id}", new { document.Id, document.Title, chunks });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(statusCode: 409, title: ex.Message);
        }
    })
    .DisableAntiforgery();

app.MapPost("/ask", async (AskRequest request, AnswerService answers, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 1000)
    {
        return Results.Problem(statusCode: 400, title: "Question must be 1-1000 characters.");
    }

    return Results.Ok(await answers.AskAsync(request.Question, request.TopK ?? 5, ct));
});

app.Run();

public sealed record AskRequest(string Question, int? TopK);

public partial class Program;
