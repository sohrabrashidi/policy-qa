namespace PolicyQa.Core.Ingestion;

/// <summary>
/// A retrievable piece of a document. <see cref="Section"/> keeps the heading path
/// ("Customer Due Diligence > Enhanced due diligence") so answers can cite
/// something a compliance officer can actually find in the original.
/// </summary>
public sealed record Chunk(string Id, string DocumentId, string DocumentTitle, string Section, string Text);

public sealed record SourceDocument(string Id, string Title, string Content);
