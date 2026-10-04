using System.Text;
using PolicyQa.Core.Retrieval;

namespace PolicyQa.Core.Answering;

public static class PromptBuilder
{
    public const string NotCovered = "The provided policy documents do not cover this.";

    public const string SystemPrompt =
        $"""
        You answer questions for compliance and operations staff using ONLY the numbered policy extracts provided.

        Rules:
        - Every factual sentence must end with the source number(s) in square brackets, e.g. [2] or [1][3].
        - If the extracts do not contain the answer, reply exactly: "{NotCovered}"
        - Do not use outside knowledge, do not guess thresholds, time limits or amounts.
        - Quote numbers, limits and deadlines exactly as written.
        - Keep it short: a direct answer first, then supporting detail if needed.
        """;

    public static string UserPrompt(string question, IReadOnlyList<ScoredChunk> sources)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Policy extracts:");
        for (var i = 0; i < sources.Count; i++)
        {
            var c = sources[i].Chunk;
            sb.AppendLine();
            sb.AppendLine($"[{i + 1}] {c.DocumentTitle} / {c.Section}");
            sb.AppendLine(c.Text);
        }

        sb.AppendLine();
        sb.AppendLine($"Question: {question}");
        return sb.ToString();
    }
}
