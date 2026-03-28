namespace shmoxy.frontend.services;

public static class ContentTypeClassifier
{
    public static bool IsApiResponse(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return true;

        var mediaType = contentType.Split(';')[0].Trim().ToLowerInvariant();

        if (mediaType is "application/json"
            or "application/xml"
            or "text/xml"
            or "text/plain"
            or "application/x-www-form-urlencoded"
            or "application/grpc")
        {
            return true;
        }

        if (mediaType.StartsWith("application/") && mediaType.EndsWith("+json"))
            return true;

        if (mediaType.StartsWith("application/") && mediaType.EndsWith("+xml"))
            return true;

        if (mediaType.StartsWith("text/") && mediaType.EndsWith("+xml"))
            return true;

        return false;
    }

    public static bool IsApiResponse(Dictionary<string, string>? responseHeaders)
    {
        if (responseHeaders is null || responseHeaders.Count == 0)
            return true;

        string? contentType = null;
        foreach (var kvp in responseHeaders)
        {
            if (kvp.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                contentType = kvp.Value;
                break;
            }
        }

        return IsApiResponse(contentType);
    }
}
