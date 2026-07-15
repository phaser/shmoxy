namespace shmoxy.frontend.models;

/// <summary>
/// Structural diff of two URLs: origin + path compared as strings, query
/// parameters aligned by name.
/// </summary>
public record UrlDiff(
    string LeftBase,
    string RightBase,
    bool BaseChanged,
    List<NamedValueDiff> QueryParams);
