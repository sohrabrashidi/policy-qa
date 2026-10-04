using PolicyQa.Core.Answering;
using PolicyQa.Core.Ingestion;
using PolicyQa.Core.Llm;
using PolicyQa.Core.Retrieval;

namespace PolicyQa.Tests;

public class AnswerServiceTests
{
    private sealed class FakeChat(string reply) : IChatModel
    {
        public string? LastUserPrompt { get; private set; }

        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
        {
            LastUserPrompt = userPrompt;
            return Task.FromResult(reply);
        }
    }

    private static async Task<HybridRetriever> Index()
    {
        var index = new HybridRetriever(new HashingEmbedder());
        var doc = new SourceDocument("rk", "Record Keeping Policy", """
            # Record Keeping Policy
            ## Transaction records
            Records of every transfer are kept for 5 years from the transaction date.
            ## Access
            Compliance has read access to all records.
            """);
        await index.AddAsync(doc, new MarkdownChunker().Split(doc), CancellationToken.None);
        return index;
    }

    [Fact]
    public async Task Llm_answer_keeps_valid_citations_and_drops_invented_ones()
    {
        var chat = new FakeChat("Transaction records are kept for 5 years [1]. See also [9].");
        var service = new AnswerService(await Index(), chat);

        var answer = await service.AskAsync("How long are transaction records kept?", 2, CancellationToken.None);

        Assert.True(answer.Grounded);
        Assert.DoesNotContain("[9]", answer.Text);
        Assert.Equal(1, Assert.Single(answer.Citations).Number);
        Assert.Contains("[1] Record Keeping Policy / Transaction records", chat.LastUserPrompt);
    }

    [Fact]
    public async Task Answer_without_citations_is_marked_ungrounded()
    {
        var service = new AnswerService(await Index(), new FakeChat("Probably around seven years."));

        var answer = await service.AskAsync("How long are records kept?", 2, CancellationToken.None);

        Assert.False(answer.Grounded);
        Assert.Empty(answer.Citations);
    }

    [Fact]
    public async Task Model_saying_not_covered_is_passed_through()
    {
        var service = new AnswerService(await Index(), new FakeChat(PromptBuilder.NotCovered));

        var answer = await service.AskAsync("What is the dress code?", 2, CancellationToken.None);

        Assert.Equal(PromptBuilder.NotCovered, answer.Text);
        Assert.False(answer.Grounded);
    }

    [Fact]
    public async Task Offline_mode_returns_matching_sentence_with_source()
    {
        var service = new AnswerService(await Index(), chat: null);

        var answer = await service.AskAsync("How long are transaction records kept?", 2, CancellationToken.None);

        Assert.Equal("extractive", answer.Mode);
        Assert.Contains("5 years", answer.Text);
        Assert.Contains("[1]", answer.Text);
    }

    [Fact]
    public async Task Unrelated_question_is_not_answered()
    {
        var service = new AnswerService(await Index(), chat: null);

        var answer = await service.AskAsync("Who won the football match?", 2, CancellationToken.None);

        Assert.Equal(PromptBuilder.NotCovered, answer.Text);
    }
}
