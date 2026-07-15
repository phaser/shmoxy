using Microsoft.EntityFrameworkCore;
using shmoxy.api.data;
using shmoxy.api.models;
using shmoxy.api.models.dto;

namespace shmoxy.api.server;

public class SavedTraceRepository : ISavedTraceRepository
{
    private readonly ProxiesDbContext _dbContext;

    public SavedTraceRepository(ProxiesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SavedTrace> SaveAsync(SavedTrace trace, CancellationToken ct = default)
    {
        trace.SavedAt = DateTime.UtcNow;
        _dbContext.SavedTraces.Add(trace);
        await _dbContext.SaveChangesAsync(ct);
        return trace;
    }

    public async Task<List<SavedTraceSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        return await _dbContext.SavedTraces
            .OrderByDescending(t => t.SavedAt)
            .Select(t => new SavedTraceSummaryDto
            {
                Id = t.Id,
                Method = t.Method,
                Url = t.Url,
                StatusCode = t.StatusCode,
                DurationMs = t.DurationMs,
                Timestamp = t.Timestamp,
                SavedAt = t.SavedAt,
                Note = t.Note,
                IsWebSocket = t.IsWebSocket,
                ResponseBodySize = t.ResponseBodyBase64 != null
                    ? (long?)(t.ResponseBodyBase64.Length * 3L / 4L)
                    : t.ResponseBody != null
                        ? (long?)t.ResponseBody.Length
                        : null
            })
            .ToListAsync(ct);
    }

    public async Task<SavedTrace?> GetAsync(string traceId, CancellationToken ct = default)
    {
        return await _dbContext.SavedTraces
            .Include(t => t.WebSocketFrames)
            .FirstOrDefaultAsync(t => t.Id == traceId, ct);
    }

    public async Task<bool> UpdateNoteAsync(string traceId, string? note, CancellationToken ct = default)
    {
        var trace = await _dbContext.SavedTraces.FindAsync([traceId], ct);
        if (trace is null)
            return false;

        trace.Note = string.IsNullOrWhiteSpace(note) ? null : note;
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string traceId, CancellationToken ct = default)
    {
        var trace = await _dbContext.SavedTraces.FindAsync([traceId], ct);
        if (trace is null)
            return false;

        _dbContext.SavedTraces.Remove(trace);
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }
}
