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
        var logEntries = request.LogEntries?.Select(ToLogEntity).ToList() ?? new List<InspectionSessionLogEntry>();
        var session = await _sessionRepository.CreateSessionAsync(request.Name.Trim(), rows, logEntries, ct);

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
        var logEntries = request.LogEntries?.Select(ToLogEntity).ToList() ?? new List<InspectionSessionLogEntry>();
        await _sessionRepository.UpdateSessionAsync(id, rows, logEntries, ct);

        var updated = await _sessionRepository.GetSessionAsync(id, ct);
        return Ok(ToResponse(updated!));
    }

    [HttpGet("{id}/logs")]
    public async Task<ActionResult<List<SessionLogEntryDto>>> GetSessionLogs(string id, CancellationToken ct)
    {
        var session = await _sessionRepository.GetSessionAsync(id, ct);
        if (session is null)
            return NotFound(new { Message = $"Session '{id}' not found" });

        var entries = await _sessionRepository.LoadLogEntriesAsync(id, ct);
        return Ok(entries.Select(ToLogDto).ToList());
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
        ResponseBody = row.ResponseBody,
        ResponseBodyBase64 = row.ResponseBodyBase64,
        ResponseContentType = row.ResponseContentType,
        IsWebSocket = row.IsWebSocket,
        WebSocketClosed = row.WebSocketClosed,
        WebSocketFrames = row.WebSocketFrames.Count > 0
            ? row.WebSocketFrames.Select(ToFrameDto).ToList()
            : null,
        TimingConnectMs = row.TimingConnectMs,
        TimingTlsMs = row.TimingTlsMs,
        TimingSendMs = row.TimingSendMs,
        TimingWaitMs = row.TimingWaitMs,
        TimingReceiveMs = row.TimingReceiveMs,
        TimingConnectionReused = row.TimingConnectionReused
    };

    private static InspectionSessionRow ToEntity(SessionRowDto dto)
    {
        var row = new InspectionSessionRow
        {
            Method = dto.Method,
            Url = dto.Url,
            StatusCode = dto.StatusCode,
            DurationMs = dto.DurationMs,
            Timestamp = dto.Timestamp,
            RequestHeaders = SerializeHeaders(dto.RequestHeaders),
            ResponseHeaders = SerializeHeaders(dto.ResponseHeaders),
            RequestBody = dto.RequestBody,
            ResponseBody = dto.ResponseBody,
            ResponseBodyBase64 = dto.ResponseBodyBase64,
            ResponseContentType = dto.ResponseContentType,
            IsWebSocket = dto.IsWebSocket,
            WebSocketClosed = dto.WebSocketClosed,
            TimingConnectMs = dto.TimingConnectMs,
            TimingTlsMs = dto.TimingTlsMs,
            TimingSendMs = dto.TimingSendMs,
            TimingWaitMs = dto.TimingWaitMs,
            TimingReceiveMs = dto.TimingReceiveMs,
            TimingConnectionReused = dto.TimingConnectionReused
        };

        if (dto.WebSocketFrames is { Count: > 0 })
        {
            row.WebSocketFrames = dto.WebSocketFrames.Select(f => new InspectionSessionWebSocketFrame
            {
                SessionRowId = row.Id,
                Timestamp = f.Timestamp,
                Direction = f.Direction,
                FrameType = f.FrameType,
                Payload = f.Payload
            }).ToList();
        }

        return row;
    }

    private static string? SerializeHeaders(List<KeyValuePair<string, string>>? headers)
    {
        if (headers is null || headers.Count == 0)
            return null;
        return JsonSerializer.Serialize(headers);
    }

    private static List<KeyValuePair<string, string>>? DeserializeHeaders(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        // Support both legacy dictionary format {"key":"value"} and new list format [{"Key":"k","Value":"v"}]
        var trimmed = json.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return dict?.Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value)).ToList();
        }
        return JsonSerializer.Deserialize<List<KeyValuePair<string, string>>>(json);
    }

    private static WebSocketFrameDto ToFrameDto(InspectionSessionWebSocketFrame frame) => new()
    {
        Timestamp = frame.Timestamp,
        Direction = frame.Direction,
        FrameType = frame.FrameType,
        Payload = frame.Payload
    };

    private static InspectionSessionLogEntry ToLogEntity(SessionLogEntryDto dto) => new()
    {
        Timestamp = dto.Timestamp,
        Level = dto.Level,
        Category = dto.Category,
        Message = dto.Message
    };

    private static SessionLogEntryDto ToLogDto(InspectionSessionLogEntry entry) => new()
    {
        Timestamp = entry.Timestamp,
        Level = entry.Level,
        Category = entry.Category,
        Message = entry.Message
    };
}
