using System.Text.Json;
using System.Xml.Linq;

namespace shmoxy.frontend.services;

public static class PayloadFormatter
{
    public static (string FormattedContent, string Language) Format(string? body, string? contentType)
    {
        if (string.IsNullOrEmpty(body))
            return (string.Empty, "plaintext");

        var language = DetectLanguage(contentType, body);

        var formatted = language switch
        {
            "json" => TryFormatJson(body),
            "xml" => TryFormatXml(body),
            _ => body
        };

        return (formatted, language);
    }

    public static string DetectLanguage(string? contentType, string? body)
    {
        if (contentType is not null)
        {
            var ct = contentType.Split(';')[0].Trim().ToLowerInvariant();

            if (ct.Contains("json"))
                return "json";
            if (ct.Contains("xml"))
                return "xml";
            if (ct.Contains("html"))
                return "html";
            if (ct.Contains("css"))
                return "css";
            if (ct.Contains("javascript"))
                return "javascript";
        }

        if (body is not null)
        {
            var trimmed = body.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
                return "json";
            if (trimmed.StartsWith('<'))
                return "xml";
        }

        return "plaintext";
    }

    private static string TryFormatJson(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return body;
        }
    }

    private static string TryFormatXml(string body)
    {
        try
        {
            var doc = XDocument.Parse(body);
            return doc.ToString();
        }
        catch
        {
            return body;
        }
    }
}
