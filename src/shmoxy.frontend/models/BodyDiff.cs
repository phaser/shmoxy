namespace shmoxy.frontend.models;

/// <summary>
/// Result of comparing two payload bodies. <see cref="Rows"/> is populated
/// only for <see cref="BodyDiffKind.Text"/>.
/// </summary>
public record BodyDiff(
    BodyDiffKind Kind,
    bool Identical,
    long LeftSize,
    long RightSize,
    string? LeftContentType,
    string? RightContentType,
    List<LineDiffRow>? Rows);
