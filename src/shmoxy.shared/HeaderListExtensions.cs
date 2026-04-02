namespace shmoxy.shared;

public static class HeaderListExtensions
{
    public static string? GetHeaderValue(this List<KeyValuePair<string, string>> headers, string key)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return headers[i].Value;
        }
        return null;
    }

    public static bool TryGetHeaderValue(this List<KeyValuePair<string, string>> headers, string key, out string value)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = headers[i].Value;
                return true;
            }
        }
        value = default!;
        return false;
    }
}
