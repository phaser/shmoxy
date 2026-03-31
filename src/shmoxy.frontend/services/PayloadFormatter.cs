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
            "html" => TryFormatHtml(body),
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
            {
                var lower = trimmed.ToLowerInvariant();
                if (lower.StartsWith("<!doctype html") || lower.StartsWith("<html"))
                    return "html";
                return "xml";
            }
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

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr"
    };

    private static readonly HashSet<string> PreserveElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "pre", "script", "style", "textarea"
    };

    private static string TryFormatHtml(string body)
    {
        try
        {
            var result = new System.Text.StringBuilder();
            var indent = 0;
            var i = 0;
            string? preserveUntilClose = null;
            var preserveStart = 0;

            while (i < body.Length)
            {
                if (body[i] == '<')
                {
                    // Find end of tag
                    var tagEnd = body.IndexOf('>', i);
                    if (tagEnd < 0)
                    {
                        result.Append(body[i..]);
                        break;
                    }

                    var tag = body[i..(tagEnd + 1)];
                    var tagName = ExtractTagName(tag);

                    // Check if we're inside a preserve block
                    if (preserveUntilClose is not null)
                    {
                        if (tag.StartsWith("</", StringComparison.Ordinal) &&
                            string.Equals(tagName, preserveUntilClose, StringComparison.OrdinalIgnoreCase))
                        {
                            // Output everything from preserve start to here verbatim
                            result.Append(body[preserveStart..(tagEnd + 1)]);
                            preserveUntilClose = null;
                            i = tagEnd + 1;
                            // Add newline after closing preserve tag
                            if (i < body.Length)
                                result.Append('\n');
                            indent = Math.Max(0, indent - 1);
                        }
                        else
                        {
                            i = tagEnd + 1;
                        }
                        continue;
                    }

                    var isClosing = tag.StartsWith("</", StringComparison.Ordinal);
                    var isSelfClosing = tag.EndsWith("/>", StringComparison.Ordinal);
                    var isVoid = VoidElements.Contains(tagName);

                    if (isClosing)
                        indent = Math.Max(0, indent - 1);

                    result.Append(new string(' ', indent * 2));
                    result.Append(tag);

                    if (!isClosing && !isSelfClosing && !isVoid && PreserveElements.Contains(tagName))
                    {
                        preserveUntilClose = tagName;
                        preserveStart = i;
                        indent++;
                        i = tagEnd + 1;
                        continue;
                    }

                    if (!isClosing && !isSelfClosing && !isVoid)
                        indent++;

                    i = tagEnd + 1;

                    // Capture inline text content between tags
                    if (i < body.Length && body[i] != '<')
                    {
                        var nextTag = body.IndexOf('<', i);
                        var text = nextTag < 0 ? body[i..] : body[i..nextTag];
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            result.Append(text);
                            i = nextTag < 0 ? body.Length : nextTag;
                            // If next tag is a closing tag for the same element, keep inline
                            if (!isClosing && !isSelfClosing && !isVoid && i < body.Length)
                            {
                                var closeTag = $"</{tagName}>";
                                if (body[i..].StartsWith(closeTag, StringComparison.OrdinalIgnoreCase))
                                {
                                    result.Append(closeTag);
                                    i += closeTag.Length;
                                    indent = Math.Max(0, indent - 1);
                                }
                            }
                        }
                        else
                        {
                            i = nextTag < 0 ? body.Length : nextTag;
                        }
                    }

                    result.Append('\n');
                }
                else
                {
                    // Text outside tags — skip whitespace between tags
                    var nextTag = body.IndexOf('<', i);
                    var text = nextTag < 0 ? body[i..] : body[i..nextTag];
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result.Append(new string(' ', indent * 2));
                        result.Append(text.Trim());
                        result.Append('\n');
                    }
                    i = nextTag < 0 ? body.Length : nextTag;
                }
            }

            return result.ToString().TrimEnd();
        }
        catch
        {
            return body;
        }
    }

    private static string ExtractTagName(string tag)
    {
        var start = tag.StartsWith("</", StringComparison.Ordinal) ? 2 : 1;
        var end = start;
        while (end < tag.Length && tag[end] != '>' && tag[end] != ' ' && tag[end] != '/' && tag[end] != '\t')
            end++;
        return tag[start..end];
    }
}
