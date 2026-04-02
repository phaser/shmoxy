namespace shmoxy.frontend.services;

public static class ImageContentTypeDetector
{
    private static readonly HashSet<string> SupportedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/svg+xml"
    };

    public static bool IsImageContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;

        var mediaType = contentType.Split(';')[0].Trim();
        return SupportedImageTypes.Contains(mediaType);
    }

    public static string BuildDataUri(string contentType, string base64Data)
    {
        var mediaType = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return $"data:{mediaType};base64,{base64Data}";
    }
}
