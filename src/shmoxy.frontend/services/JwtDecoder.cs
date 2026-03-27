using System.Text;
using System.Text.Json;

namespace shmoxy.frontend.services;

public static class JwtDecoder
{
    public static bool IsJwt(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            return false;

        // Check that header and payload are valid base64url
        try
        {
            Base64UrlDecode(parts[0]);
            Base64UrlDecode(parts[1]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? ExtractBearerToken(string headerValue)
    {
        if (headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return headerValue["Bearer ".Length..].Trim();
        return null;
    }

    public static JwtDecodeResult? Decode(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        try
        {
            var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));

            return new JwtDecodeResult
            {
                Header = FormatJson(headerJson),
                Payload = FormatJson(payloadJson),
                Signature = parts[2]
            };
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    private static string FormatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }
}

public class JwtDecodeResult
{
    public string Header { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
}
