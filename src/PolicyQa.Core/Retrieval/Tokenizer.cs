using System.Text.RegularExpressions;

namespace PolicyQa.Core.Retrieval;

public static partial class Tokenizer
{
    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "and", "are", "as", "at", "be", "by", "can", "do", "does", "for", "from", "has", "have",
        "how", "i", "if", "in", "is", "it", "its", "must", "of", "on", "or", "our", "shall", "should",
        "that", "the", "their", "this", "to", "we", "what", "when", "which", "who", "will", "with", "you",
    ];

    public static IEnumerable<string> Tokens(string text) =>
        WordPattern().Matches(text.ToLowerInvariant())
            .Select(m => Stem(m.Value))
            .Where(t => t.Length > 1 && !StopWords.Contains(t));

    // Deliberately tiny stemmer: enough to match "transfers"/"transfer" and
    // "verified"/"verify" without pulling in a library.
    private static string Stem(string word)
    {
        if (word.Length > 5 && word.EndsWith("ies", StringComparison.Ordinal))
        {
            return word[..^3] + "y";
        }

        if (word.Length > 5 && word.EndsWith("ied", StringComparison.Ordinal))
        {
            return word[..^3] + "y";
        }

        foreach (var suffix in new[] { "ing", "ed", "es", "s" })
        {
            if (word.Length > suffix.Length + 3 && word.EndsWith(suffix, StringComparison.Ordinal) && !word.EndsWith("ss", StringComparison.Ordinal))
            {
                return word[..^suffix.Length];
            }
        }

        return word;
    }

    [GeneratedRegex(@"[a-z0-9]+(?:[-'][a-z0-9]+)*")]
    private static partial Regex WordPattern();
}
