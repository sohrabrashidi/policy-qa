using System.Text.RegularExpressions;

namespace PolicyQa.Core.Answering;

public static partial class CitationParser
{
    /// <summary>Source numbers referenced in the answer that actually exist in the prompt.</summary>
    public static IReadOnlyList<int> ValidReferences(string answer, int sourceCount) =>
        ReferencePattern().Matches(answer)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Where(n => n >= 1 && n <= sourceCount)
            .Distinct()
            .Order()
            .ToList();

    /// <summary>Removes references to sources that were never given to the model.</summary>
    public static string StripInvalid(string answer, int sourceCount) =>
        ReferencePattern().Replace(answer, m =>
            int.Parse(m.Groups[1].Value) is var n && n >= 1 && n <= sourceCount ? m.Value : string.Empty);

    [GeneratedRegex(@"\[(\d{1,2})\]")]
    private static partial Regex ReferencePattern();
}
