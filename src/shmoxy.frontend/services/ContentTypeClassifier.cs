namespace shmoxy.frontend.services;

public static class ContentTypeClassifier
{
    private static readonly HashSet<string> StaticResourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".cjs",
        ".css",
        ".html", ".htm",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".svg", ".bmp", ".avif",
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".wasm",
        ".map",
        ".mp3", ".mp4", ".webm", ".ogg", ".wav",
        ".pdf",
        ".zip", ".gz", ".br", ".tar", ".bz2",
    };

    private static readonly HashSet<string> BlobResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "x-ms-blob-type",
        "x-amz-request-id",
        "x-goog-stored-content-length",
    };

    public static bool IsApiCall(string? url, Dictionary<string, string>? requestHeaders, Dictionary<string, string>? responseHeaders)
    {
        if (HasStaticResourceExtension(url))
            return false;

        if (HasBlobStorageHeaders(responseHeaders))
            return false;

        if (HasFileDownloadDisposition(responseHeaders))
            return false;

        return IsApiResponse(responseHeaders);
    }

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

    internal static bool HasStaticResourceExtension(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        // Strip query string and fragment
        var pathEnd = url.AsSpan();
        var queryIndex = pathEnd.IndexOf('?');
        if (queryIndex >= 0)
            pathEnd = pathEnd[..queryIndex];
        var fragmentIndex = pathEnd.IndexOf('#');
        if (fragmentIndex >= 0)
            pathEnd = pathEnd[..fragmentIndex];

        // Find last dot in the path
        var lastDot = pathEnd.LastIndexOf('.');
        if (lastDot < 0)
            return false;

        // Also ensure the dot is after the last slash (it's a file extension, not part of a hostname)
        var lastSlash = pathEnd.LastIndexOf('/');
        if (lastDot < lastSlash)
            return false;

        var extension = pathEnd[lastDot..].ToString();
        return StaticResourceExtensions.Contains(extension);
    }

    private static bool HasBlobStorageHeaders(Dictionary<string, string>? headers)
    {
        if (headers is null)
            return false;

        foreach (var kvp in headers)
        {
            if (BlobResponseHeaders.Contains(kvp.Key))
                return true;
        }

        return false;
    }

    private static bool HasFileDownloadDisposition(Dictionary<string, string>? headers)
    {
        if (headers is null)
            return false;

        foreach (var kvp in headers)
        {
            if (kvp.Key.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase)
                && kvp.Value.Contains("attachment", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
