namespace shmoxy.frontend.models;

public record DetectorInfo(string Id, string Name, bool Enabled);

public record PassthroughSuggestionDto(
    DateTime Timestamp,
    string Host,
    string DetectorId,
    string DetectorName,
    string Reason);
