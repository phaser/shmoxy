using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace shmoxy.frontend.services;

public static class HarExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Export(IReadOnlyList<InspectionRow> rows)
    {
        var har = new HarRoot
        {
            Log = new HarLog
            {
                Version = "1.2",
                Creator = new HarCreator
                {
                    Name = "shmoxy",
                    Version = "1.0"
                },
                Entries = rows
                    .Where(r => !r.IsWebSocket)
                    .Select(ToEntry)
                    .ToList()
            }
        };

        return JsonSerializer.Serialize(har, JsonOptions);
    }

    private static HarEntry ToEntry(InspectionRow row)
    {
        var entry = new HarEntry
        {
            StartedDateTime = row.Timestamp.ToString("O"),
            Time = row.Duration?.TotalMilliseconds ?? 0,
            Request = new HarRequest
            {
                Method = row.Method,
                Url = row.Url,
                HttpVersion = "HTTP/1.1",
                Headers = row.RequestHeaders.Select(h => new HarNameValue { Name = h.Key, Value = h.Value }).ToList(),
                QueryString = ParseQueryString(row.Url),
                BodySize = row.RequestBody != null ? System.Text.Encoding.UTF8.GetByteCount(row.RequestBody) : 0,
                HeadersSize = -1,
                PostData = !string.IsNullOrEmpty(row.RequestBody)
                    ? new HarPostData
                    {
                        MimeType = row.RequestHeaders
                            .FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value
                            ?? "application/octet-stream",
                        Text = row.RequestBody
                    }
                    : null
            },
            Response = new HarResponse
            {
                Status = row.StatusCode ?? 0,
                StatusText = "",
                HttpVersion = "HTTP/1.1",
                Headers = row.ResponseHeaders.Select(h => new HarNameValue { Name = h.Key, Value = h.Value }).ToList(),
                Content = new HarContent
                {
                    Size = row.ResponseBody != null ? System.Text.Encoding.UTF8.GetByteCount(row.ResponseBody) : 0,
                    MimeType = row.ResponseContentType ?? "application/octet-stream",
                    Text = row.ResponseBodyBase64 ?? row.ResponseBody,
                    Encoding = row.ResponseBodyBase64 != null ? "base64" : null
                },
                BodySize = row.ResponseBody != null ? System.Text.Encoding.UTF8.GetByteCount(row.ResponseBody) : 0,
                HeadersSize = -1,
                RedirectURL = ""
            },
            Cache = new HarCache(),
            Timings = new HarTimings
            {
                Send = 0,
                Wait = row.Duration?.TotalMilliseconds ?? 0,
                Receive = 0
            }
        };

        return entry;
    }

    private static List<HarNameValue> ParseQueryString(string url)
    {
        var result = new List<HarNameValue>();
        var queryIndex = url.IndexOf('?');
        if (queryIndex < 0) return result;

        var query = url[(queryIndex + 1)..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex >= 0)
            {
                result.Add(new HarNameValue
                {
                    Name = Uri.UnescapeDataString(pair[..eqIndex]),
                    Value = Uri.UnescapeDataString(pair[(eqIndex + 1)..])
                });
            }
            else
            {
                result.Add(new HarNameValue { Name = Uri.UnescapeDataString(pair), Value = "" });
            }
        }

        return result;
    }

    // HAR 1.2 spec types
    private record HarRoot { public HarLog Log { get; init; } = new(); }
    private record HarLog
    {
        public string Version { get; init; } = "1.2";
        public HarCreator Creator { get; init; } = new();
        public List<HarEntry> Entries { get; init; } = new();
    }
    private record HarCreator
    {
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
    }
    private record HarEntry
    {
        public string StartedDateTime { get; init; } = "";
        public double Time { get; init; }
        public HarRequest Request { get; init; } = new();
        public HarResponse Response { get; init; } = new();
        public HarCache Cache { get; init; } = new();
        public HarTimings Timings { get; init; } = new();
    }
    private record HarRequest
    {
        public string Method { get; init; } = "";
        public string Url { get; init; } = "";
        public string HttpVersion { get; init; } = "";
        public List<HarNameValue> Headers { get; init; } = new();
        public List<HarNameValue> QueryString { get; init; } = new();
        public HarPostData? PostData { get; init; }
        public long BodySize { get; init; }
        public int HeadersSize { get; init; }
    }
    private record HarResponse
    {
        public int Status { get; init; }
        public string StatusText { get; init; } = "";
        public string HttpVersion { get; init; } = "";
        public List<HarNameValue> Headers { get; init; } = new();
        public HarContent Content { get; init; } = new();
        public string RedirectURL { get; init; } = "";
        public long BodySize { get; init; }
        public int HeadersSize { get; init; }
    }
    private record HarContent
    {
        public long Size { get; init; }
        public string MimeType { get; init; } = "";
        public string? Text { get; init; }
        public string? Encoding { get; init; }
    }
    private record HarPostData
    {
        public string MimeType { get; init; } = "";
        public string Text { get; init; } = "";
    }
    private record HarCache;
    private record HarTimings
    {
        public double Send { get; init; }
        public double Wait { get; init; }
        public double Receive { get; init; }
    }
    private record HarNameValue
    {
        public string Name { get; init; } = "";
        public string Value { get; init; } = "";
    }
}
