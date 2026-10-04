using PolicyQa.Core.Ingestion;

namespace PolicyQa.Tests;

public class ChunkerTests
{
    [Fact]
    public void Chunks_keep_their_heading_path()
    {
        var doc = new SourceDocument("d", "Policy", """
            # Policy

            ## 4. Enhanced due diligence

            ### 4.1 When it applies

            Applies to PEPs.

            ### 4.2 What it involves

            Source of funds is required.
            """);

        var chunks = new MarkdownChunker().Split(doc);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("4. Enhanced due diligence > 4.1 When it applies", chunks[0].Section);
        Assert.Equal("4. Enhanced due diligence > 4.2 What it involves", chunks[1].Section);
    }

    [Fact]
    public void A_chunk_never_mixes_two_sections()
    {
        var doc = new SourceDocument("d", "Policy", "## A\nalpha text\n## B\nbeta text");

        var chunks = new MarkdownChunker().Split(doc);

        Assert.DoesNotContain("beta", chunks[0].Text);
        Assert.DoesNotContain("alpha", chunks[1].Text);
    }

    [Fact]
    public void Long_sections_are_split_with_overlap()
    {
        var sentence = "Every transfer above the limit is reviewed by compliance staff. ";
        var doc = new SourceDocument("d", "Policy", "## Long\n" + string.Concat(Enumerable.Repeat(sentence, 40)));

        var chunks = new MarkdownChunker(maxChars: 400, overlapChars: 80).Split(doc);

        Assert.True(chunks.Count > 3);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 400));
        Assert.All(chunks, c => Assert.Equal("Long", c.Section));
    }

    [Fact]
    public void Unsupported_file_types_are_rejected()
    {
        Assert.Throws<NotSupportedException>(() => DocumentLoader.FromText("policy.pdf", "x"));
    }

    [Fact]
    public void Title_comes_from_the_first_heading()
    {
        var doc = DocumentLoader.FromText("cdd_policy.md", "# Customer Due Diligence Policy\n\ntext");

        Assert.Equal("Customer Due Diligence Policy", doc.Title);
        Assert.Equal("cdd-policy", doc.Id);
    }
}
