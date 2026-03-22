namespace shmoxy.frontend.services;

public record InspectionRequestInfo(
    string Method,
    string Url,
    int StatusCode,
    DateTime Timestamp,
    Dictionary<string, string> RequestHeaders,
    Dictionary<string, string> ResponseHeaders,
    string? RequestBody,
    string? ResponseBody
);
