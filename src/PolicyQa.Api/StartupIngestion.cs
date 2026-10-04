using PolicyQa.Core.Ingestion;
using PolicyQa.Core.Retrieval;

namespace PolicyQa.Api;

/// <summary>Indexes everything in the configured folder when the app starts.</summary>
public sealed class StartupIngestion(
    IConfiguration config,
    IHostEnvironment env,
    MarkdownChunker chunker,
    HybridRetriever index,
    ILogger<StartupIngestion> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var folder = config["DocumentsFolder"] ?? "sample-docs";
        if (!Path.IsPathRooted(folder))
        {
            folder = Path.Combine(AppContext.BaseDirectory, folder);
        }

        foreach (var doc in await DocumentLoader.FromFolderAsync(folder, ct))
        {
            var count = await index.AddAsync(doc, chunker.Split(doc), ct);
            log.LogInformation("Indexed {Title} ({Chunks} chunks)", doc.Title, count);
        }

        log.LogInformation("{Env}: {Docs} documents, {Chunks} chunks ready", env.EnvironmentName, index.Documents.Count, index.ChunkCount);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
