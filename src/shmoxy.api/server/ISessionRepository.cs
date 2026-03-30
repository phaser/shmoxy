using shmoxy.api.models;

namespace shmoxy.api.server;

public interface ISessionRepository
{
    Task<InspectionSession> CreateSessionAsync(string name, List<InspectionSessionRow> rows, CancellationToken ct = default);
    Task<InspectionSession> CreateSessionAsync(string name, List<InspectionSessionRow> rows, List<InspectionSessionLogEntry> logEntries, CancellationToken ct = default);
    Task<List<InspectionSession>> ListSessionsAsync(CancellationToken ct = default);
    Task<List<InspectionSessionRow>> LoadRowsAsync(string sessionId, CancellationToken ct = default);
    Task<List<InspectionSessionLogEntry>> LoadLogEntriesAsync(string sessionId, CancellationToken ct = default);
    Task<InspectionSession?> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task UpdateSessionAsync(string sessionId, List<InspectionSessionRow> rows, CancellationToken ct = default);
    Task UpdateSessionAsync(string sessionId, List<InspectionSessionRow> rows, List<InspectionSessionLogEntry> logEntries, CancellationToken ct = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken ct = default);
}
