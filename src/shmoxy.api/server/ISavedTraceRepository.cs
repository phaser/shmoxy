using shmoxy.api.models;
using shmoxy.api.models.dto;

namespace shmoxy.api.server;

public interface ISavedTraceRepository
{
    Task<SavedTrace> SaveAsync(SavedTrace trace, CancellationToken ct = default);
    Task<List<SavedTraceSummaryDto>> ListAsync(CancellationToken ct = default);
    Task<SavedTrace?> GetAsync(string traceId, CancellationToken ct = default);
    Task<bool> UpdateNoteAsync(string traceId, string? note, CancellationToken ct = default);
    Task<bool> DeleteAsync(string traceId, CancellationToken ct = default);
}
