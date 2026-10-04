using PolicyQa.Core.Ingestion;
using PolicyQa.Core.Llm;
using PolicyQa.Core.Retrieval;

namespace PolicyQa.Tests;

public class RetrievalTests
{
    private static async Task<HybridRetriever> SampleIndex()
    {
        var index = new HybridRetriever(new HashingEmbedder());
        var chunker = new MarkdownChunker();
        var folder = Path.Combine(AppContext.BaseDirectory, "sample-docs");

        foreach (var doc in await DocumentLoader.FromFolderAsync(folder, CancellationToken.None))
        {
            await index.AddAsync(doc, chunker.Split(doc), CancellationToken.None);
        }

        return index;
    }

    [Theory]
    [InlineData("What is the retention period for transaction records?", "Record Keeping", "Transaction records")]
    [InlineData("What do we need for a politically exposed person?", "Customer Due Diligence", "Enhanced due diligence")]
    [InlineData("How fast must a held transfer be reviewed?", "Transaction Monitoring", "Automatic holds")]
    [InlineData("What documents are needed for a corporate customer?", "Customer Due Diligence", "Companies")]
    public async Task Top_result_comes_from_the_right_section(string question, string document, string section)
    {
        var index = await SampleIndex();

        var top = (await index.SearchAsync(question, 3, CancellationToken.None))[0].Chunk;

        Assert.Contains(document, top.DocumentTitle);
        Assert.Contains(section, top.Section);
    }

    [Fact]
    public async Task Same_document_cannot_be_indexed_twice()
    {
        var index = new HybridRetriever(new HashingEmbedder());
        var doc = new SourceDocument("a", "A", "## S\ntext here");
        var chunks = new MarkdownChunker().Split(doc);

        await index.AddAsync(doc, chunks, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => index.AddAsync(doc, chunks, CancellationToken.None));
    }

    [Fact]
    public void Bm25_prefers_rare_terms()
    {
        var bm25 = new Bm25Index();
        bm25.Add(new Chunk("1", "d", "D", "s", "the customer sends money every week"));
        bm25.Add(new Chunk("2", "d", "D", "s", "the customer is a PEP and needs approval"));
        bm25.Add(new Chunk("3", "d", "D", "s", "the customer sends money to family"));

        var results = bm25.Search("customer PEP", 3);

        Assert.Equal("2", results[0].Chunk.Id);
    }
}
