using System.Text.RegularExpressions;
using PolicyQa.Core.Llm;
using PolicyQa.Core.Retrieval;

namespace PolicyQa.Core.Answering;

public sealed partial class AnswerService(HybridRetriever retriever, IChatModel? chat)
{
    private const int ExcerptLength = 280;

    public async Task<Answer> AskAsync(string question, int topK, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("Question is empty.", nameof(question));
        }

        topK = Math.Clamp(topK, 1, 10);
        var sources = await retriever.SearchAsync(question, topK, ct);

        if (sources.Count == 0)
        {
            return new Answer(question, PromptBuilder.NotCovered, [], Grounded: false, Mode: "none");
        }

        return chat is null
            ? Extractive(question, sources)
            : await Generative(question, sources, ct);
    }

    private async Task<Answer> Generative(string question, IReadOnlyList<ScoredChunk> sources, CancellationToken ct)
    {
        var raw = await chat!.CompleteAsync(PromptBuilder.SystemPrompt, PromptBuilder.UserPrompt(question, sources), ct);

        if (raw.Contains(PromptBuilder.NotCovered, StringComparison.OrdinalIgnoreCase))
        {
            return new Answer(question, PromptBuilder.NotCovered, [], Grounded: false, Mode: "llm");
        }

        var text = CitationParser.StripInvalid(raw, sources.Count);
        var used = CitationParser.ValidReferences(text, sources.Count);

        // An answer with no valid citation is treated as ungrounded, even if it sounds right.
        return new Answer(question, text, ToCitations(used, sources), Grounded: used.Count > 0, Mode: "llm");
    }

    /// <summary>
    /// No LLM configured: return the best matching sentences with their sources.
    /// Less fluent, but still useful and never makes anything up.
    /// </summary>
    private static Answer Extractive(string question, IReadOnlyList<ScoredChunk> sources)
    {
        var queryTerms = Tokenizer.Tokens(question).ToHashSet();
        var picked = sources
            .Take(3)
            .Select((s, i) => (Number: i + 1, Sentence: BestSentence(s.Chunk.Text, queryTerms)))
            .Where(x => x.Sentence.Overlap > 0)
            .ToList();

        // Keep the best match, plus other sources only if they match the question just as well.
        if (picked.Count > 1)
        {
            var best = picked.Max(p => p.Sentence.Overlap);
            picked = picked.Where((p, i) => i == 0 || p.Sentence.Overlap >= best).ToList();
        }

        if (picked.Count == 0)
        {
            return new Answer(question, PromptBuilder.NotCovered, [], Grounded: false, Mode: "extractive");
        }

        var text = string.Join(" ", picked.Select(p => $"{p.Sentence.Text} [{p.Number}]"));
        return new Answer(
            question,
            text,
            ToCitations(picked.Select(p => p.Number).ToList(), sources),
            Grounded: true,
            Mode: "extractive");
    }

    private static (string Text, int Overlap) BestSentence(string text, HashSet<string> queryTerms) =>
        SentenceSplit().Split(text)
            .Select(s => s.Trim())
            .Where(s => s.Length > 20)
            .Select(s => (Text: s, Overlap: Tokenizer.Tokens(s).Distinct().Count(queryTerms.Contains)))
            .OrderByDescending(s => s.Overlap)
            .ThenBy(s => s.Text.Length)
            .FirstOrDefault();

    private static List<Citation> ToCitations(IReadOnlyList<int> numbers, IReadOnlyList<ScoredChunk> sources) =>
        numbers
            .Select(n =>
            {
                var chunk = sources[n - 1].Chunk;
                var excerpt = chunk.Text.Length > ExcerptLength ? chunk.Text[..ExcerptLength].TrimEnd() + "..." : chunk.Text;
                return new Citation(n, chunk.DocumentTitle, chunk.Section, excerpt);
            })
            .ToList();

    [GeneratedRegex(@"(?<=[.!?])\s+|\n+")]
    private static partial Regex SentenceSplit();
}
