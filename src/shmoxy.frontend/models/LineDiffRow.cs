namespace shmoxy.frontend.models;

/// <summary>
/// One row of a side-by-side line diff. A null side means the line only
/// exists on the other side (added/removed).
/// </summary>
public record LineDiffRow(string? LeftLine, string? RightLine, DiffKind Kind);
