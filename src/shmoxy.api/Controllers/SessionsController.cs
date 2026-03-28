using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using shmoxy.api.models;
using shmoxy.api.models.dto;
using shmoxy.api.server;

namespace shmoxy.api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly ISessionRepository _sessionRepository;

    public SessionsController(ISessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;
    }

    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<SessionResponse>> CreateSession(
        [FromBody] CreateSessionRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { Message = "Session name is required" });

        var rows = request.Rows.Select(ToEntity).ToList();
        var session = await _sessionRepository.CreateSessionAsync(request.Name.Trim(), rows, ct);

        return CreatedAtAction(nameof(GetSession), new { id = session.Id }, ToResponse(session));
    }

    [HttpGet]
    public async Task<ActionResult<List<SessionResponse>>> ListSessions(CancellationToken ct)
    {
        var sessions = await _sessionRepository.ListSessionsAsync(ct);
        return Ok(sessions.Select(ToResponse).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<List<SessionRowDto>>> GetSession(string id, CancellationToken ct)
    {
        var session = await _sessionRepository.GetSessionAsync(id, ct);
        if (session is null)
            return NotFound(new { Message = $"Session '{id}' not found" });

        var rows = await _sessionRepository.LoadRowsAsync(id, ct);
        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpPut("{id}")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<SessionResponse>> UpdateSession(
        string id,
        [FromBody] UpdateSessionRequest request,
        CancellationToken ct)
    {
        var session = await _sessionRepository.GetSessionAsync(id, ct);
        if (session is null)
            return NotFound(new { Message = $"Session '{id}' not found" });

        var rows = request.Rows.Select(ToEntity).ToList();
        await _sessionRepository.UpdateSessionAsync(id, rows, ct);

        var updated = await _sessionRepository.GetSessionAsync(id, ct);
        return Ok(ToResponse(updated!));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteSession(string id, CancellationToken ct)
    {
        var session = await _sessionRepository.GetSessionAsync(id, ct);
        if (session is null)
            return NotFound(new { Message = $"Session '{id}' not found" });

        await _sessionRepository.DeleteSessionAsync(id, ct);
        return NoContent();
    }

    private static SessionResponse ToResponse(InspectionSession session) => new()
    {
        Id = session.Id,
        Name = session.Name,
        RowCount = session.RowCount,
        CreatedAt = session.CreatedAt,
        UpdatedAt = session.UpdatedAt
    };

    private static SessionRowDto ToDto(InspectionSessionRow row) => new()
    {
        Method = row.Method,
        Url = row.Url,
        StatusCode = row.StatusCode,
        DurationMs = row.DurationMs,
        Timestamp = row.Timestamp,
        RequestHeaders = DeserializeHeaders(row.RequestHeaders),
        ResponseHeaders = DeserializeHeaders(row.ResponseHeaders),
        RequestBody = row.RequestBody,
        ResponseBody = row.ResponseBody
    };

    private static InspectionSessionRow ToEntity(SessionRowDto dto) => new()
    {
        Method = dto.Method,
        Url = dto.Url,
        StatusCode = dto.StatusCode,
        DurationMs = dto.DurationMs,
        Timestamp = dto.Timestamp,
        RequestHeaders = SerializeHeaders(dto.RequestHeaders),
        ResponseHeaders = SerializeHeaders(dto.ResponseHeaders),
        RequestBody = dto.RequestBody,
        ResponseBody = dto.ResponseBody
    };

    private static string? SerializeHeaders(Dictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
            return null;
        return JsonSerializer.Serialize(headers);
    }

    private static Dictionary<string, string>? DeserializeHeaders(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    }
}
