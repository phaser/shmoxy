namespace shmoxy.frontend.models;

/// <summary>
/// One aligned entry in a name/value diff (headers or query parameters):
/// present on the left, the right, or both sides.
/// </summary>
public record NamedValueDiff(string Name, string? LeftValue, string? RightValue, DiffKind Kind);
