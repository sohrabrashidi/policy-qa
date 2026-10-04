namespace PolicyQa.Core.Answering;

public sealed record Citation(int Number, string Document, string Section, string Excerpt);

public sealed record Answer(
    string Question,
    string Text,
    IReadOnlyList<Citation> Citations,
    bool Grounded,
    string Mode);
