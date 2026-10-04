using System.Text.RegularExpressions;

namespace PolicyQa.Core.Ingestion;

public static partial class DocumentLoader
{
    public static readonly string[] SupportedExtensions = [".md", ".markdown", ".txt"];

    public static SourceDocument FromText(string fileName, string content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
        {
            throw new NotSupportedException(
                $"'{extension}' files are not supported yet. Convert to Markdown or plain text first.");
        }

        var title = FirstHeading().Match(content) is { Success: true } m
            ? m.Groups[1].Value.Trim()
            : Path.GetFileNameWithoutExtension(fileName).Replace('-', ' ').Replace('_', ' ');

        return new SourceDocument(Slug(Path.GetFileNameWithoutExtension(fileName)), title, content);
    }

    public static async Task<IReadOnlyList<SourceDocument>> FromFolderAsync(string folder, CancellationToken ct)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var docs = new List<SourceDocument>();
        foreach (var path in Directory.EnumerateFiles(folder).Where(p => SupportedExtensions.Contains(Path.GetExtension(p).ToLowerInvariant())).Order())
        {
            docs.Add(FromText(Path.GetFileName(path), await File.ReadAllTextAsync(path, ct)));
        }

        return docs;
    }

    private static string Slug(string name) => NonSlug().Replace(name.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex FirstHeading();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();
}
