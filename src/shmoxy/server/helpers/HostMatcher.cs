namespace shmoxy.server.helpers;

/// <summary>
/// Matches hostnames against patterns with glob support.
/// Supports exact matches ("example.com") and wildcard patterns ("*.example.com").
/// </summary>
public static class HostMatcher
{
    /// <summary>
    /// Checks if the given host matches any of the provided patterns.
    /// </summary>
    public static bool IsMatch(string host, IReadOnlyList<string> patterns)
    {
        return patterns.Any(t => IsMatch(host, t));
    }

    /// <summary>
    /// Checks if the given host matches a single pattern.
    /// Patterns support:
    ///   - Exact match: "example.com" matches only "example.com"
    ///   - Wildcard prefix: "*.example.com" matches "foo.example.com" but not "example.com"
    /// </summary>
    public static bool IsMatch(string host, string pattern)
    {
        if (string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase))
            return true;

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..]; // ".example.com"
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
