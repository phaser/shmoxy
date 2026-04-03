using System.Text;

namespace shmoxy.frontend.services;

public static class CurlExporter
{
    public static string GenerateCommand(InspectionRow row)
    {
        var sb = new StringBuilder();
        sb.Append("curl");

        if (row.Method != "GET")
            sb.Append($" -X {row.Method}");

        sb.Append($" '{EscapeSingleQuote(row.Url)}'");

        foreach (var header in row.RequestHeaders)
            sb.Append($" \\\n  -H '{EscapeSingleQuote(header.Key)}: {EscapeSingleQuote(header.Value)}'");

        if (!string.IsNullOrEmpty(row.RequestBody))
            sb.Append($" \\\n  -d '{EscapeSingleQuote(row.RequestBody)}'");

        return sb.ToString();
    }

    private static string EscapeSingleQuote(string value)
    {
        return value.Replace("'", "'\\''");
    }
}
