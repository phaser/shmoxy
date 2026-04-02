namespace shmoxy.frontend.services;

public record InspectionRequestInfo(
    string Method,
    string Url,
    int StatusCode,
    DateTime Timestamp,
    List<KeyValuePair<string, string>> RequestHeaders,
    List<KeyValuePair<string, string>> ResponseHeaders,
    string? RequestBody,
    string? ResponseBody
);
