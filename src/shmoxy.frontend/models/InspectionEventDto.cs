namespace shmoxy.frontend.models;

public record InspectionEventDto(
    DateTime Timestamp,
    string EventType,
    string Method,
    string Url,
    int? StatusCode,
    List<KeyValuePair<string, string>>? Headers = null,
    byte[]? Body = null,
    string? CorrelationId = null,
    string? FrameType = null,
    string? Direction = null,
    bool? IsWebSocket = null
);
