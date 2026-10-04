using System.Text;
using System.Text.RegularExpressions;

namespace PolicyQa.Core.Ingestion;

/// <summary>
/// Splits Markdown / plain text into chunks that never cross a heading boundary.
/// Policy documents are written in numbered sections, and mixing two sections
/// in one chunk is the main cause of wrong-but-confident answers.
/// </summary>
public sealed partial class MarkdownChunker(int maxChars = 900, int overlapChars = 150)
{
    public IReadOnlyList<Chunk> Split(SourceDocument document)
    {
        var chunks = new List<Chunk>();
        var headings = new List<(int Level, string Text)>();
        var buffer = new StringBuilder();

        foreach (var rawLine in document.Content.Replace("\r\n", "\n").Split('\n'))
        {
            var match = HeadingPattern().Match(rawLine);
            if (match.Success)
            {
                Flush();
                var level = match.Groups[1].Value.Length;
                headings.RemoveAll(h => h.Level >= level);
                headings.Add((level, match.Groups[2].Value.Trim()));
                continue;
            }

            buffer.AppendLine(rawLine);
        }

        Flush();
        return chunks;

        void Flush()
        {
            var text = Normalise(buffer.ToString());
            buffer.Clear();
            if (text.Length == 0)
            {
                return;
            }

            // The H1 is the document title, so it is left out of the section path.
            var section = string.Join(" > ", headings.Where(h => h.Level > 1).Select(h => h.Text));
            if (section.Length == 0)
            {
                section = "Introduction";
            }

            foreach (var piece in Window(text))
            {
                chunks.Add(new Chunk($"{document.Id}#{chunks.Count}", document.Id, document.Title, section, piece));
            }
        }
    }

    private IEnumerable<string> Window(string text)
    {
        if (text.Length <= maxChars)
        {
            yield return text;
            yield break;
        }

        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(start + maxChars, text.Length);
            if (end < text.Length)
            {
                // Prefer to cut at a sentence end, then at a space.
                var cut = text.LastIndexOfAny(['.', ';', '\n'], end - 1, end - start);
                if (cut <= start + maxChars / 2)
                {
                    cut = text.LastIndexOf(' ', end - 1, end - start);
                }

                if (cut > start)
                {
                    end = cut + 1;
                }
            }

            yield return text[start..end].Trim();

            if (end >= text.Length)
            {
                yield break;
            }

            var next = end - overlapChars;
            var space = next > start ? text.IndexOf(' ', next) : -1;
            start = space > start && space < end ? space + 1 : end;
        }
    }

    private static string Normalise(string text) =>
        MultiBlankLines().Replace(text, "\n\n").Trim();

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiBlankLines();
}
