using Microsoft.EntityFrameworkCore;
using shmoxy.api.data;
using shmoxy.api.models;

namespace shmoxy.api.server;

public class SessionRepository : ISessionRepository
{
    private readonly ProxiesDbContext _dbContext;

    public SessionRepository(ProxiesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<InspectionSession> CreateSessionAsync(string name, List<InspectionSessionRow> rows, CancellationToken ct = default)
    {
        var session = new InspectionSession
        {
            Name = name,
            RowCount = rows.Count,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var row in rows)
        {
            row.SessionId = session.Id;
        }

        _dbContext.InspectionSessions.Add(session);
        _dbContext.InspectionSessionRows.AddRange(rows);
        await _dbContext.SaveChangesAsync(ct);

        return session;
    }

    public async Task<List<InspectionSession>> ListSessionsAsync(CancellationToken ct = default)
    {
        return await _dbContext.InspectionSessions
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<InspectionSession?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        return await _dbContext.InspectionSessions.FindAsync([sessionId], ct);
    }

    public async Task<List<InspectionSessionRow>> LoadRowsAsync(string sessionId, CancellationToken ct = default)
    {
        return await _dbContext.InspectionSessionRows
            .Where(r => r.SessionId == sessionId)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);
    }

    public async Task UpdateSessionAsync(string sessionId, List<InspectionSessionRow> rows, CancellationToken ct = default)
    {
        var session = await _dbContext.InspectionSessions.FindAsync([sessionId], ct)
            ?? throw new KeyNotFoundException($"Session '{sessionId}' not found");

        var existingRows = await _dbContext.InspectionSessionRows
            .Where(r => r.SessionId == sessionId)
            .ToListAsync(ct);

        _dbContext.InspectionSessionRows.RemoveRange(existingRows);

        foreach (var row in rows)
        {
            row.SessionId = sessionId;
        }

        _dbContext.InspectionSessionRows.AddRange(rows);

        session.RowCount = rows.Count;
        session.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _dbContext.InspectionSessions.FindAsync([sessionId], ct);
        if (session is null)
            return;

        _dbContext.InspectionSessions.Remove(session);
        await _dbContext.SaveChangesAsync(ct);
    }
}
