namespace shmoxy.frontend.models;

public record InspectionEventDto(
    DateTime Timestamp,
    string EventType,
    string Method,
    string Url,
    int? StatusCode,
    Dictionary<string, string>? Headers = null,
    byte[]? Body = null
);
