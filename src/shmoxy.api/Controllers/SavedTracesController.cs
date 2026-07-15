using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using shmoxy.api.models;
using shmoxy.api.models.dto;
using shmoxy.api.server;

namespace shmoxy.api.Controllers;

[ApiController]
[Route("api/saved-traces")]
public class SavedTracesController : ControllerBase
{
    private readonly ISavedTraceRepository _savedTraceRepository;

    public SavedTracesController(ISavedTraceRepository savedTraceRepository)
    {
        _savedTraceRepository = savedTraceRepository;
    }

    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<SavedTraceSummaryDto>> SaveTrace(
        [FromBody] SavedTraceDto request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Method))
            return BadRequest(new { Message = "Method is required" });
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new { Message = "Url is required" });

        var trace = await _savedTraceRepository.SaveAsync(ToEntity(request), ct);

        return CreatedAtAction(nameof(GetTrace), new { id = trace.Id }, ToSummary(trace));
    }

    [HttpGet]
    public async Task<ActionResult<List<SavedTraceSummaryDto>>> ListTraces(CancellationToken ct)
    {
        var summaries = await _savedTraceRepository.ListAsync(ct);
        return Ok(summaries);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SavedTraceDto>> GetTrace(string id, CancellationToken ct)
    {
        var trace = await _savedTraceRepository.GetAsync(id, ct);
        if (trace is null)
            return NotFound(new { Message = $"Saved trace '{id}' not found" });

        return Ok(ToDto(trace));
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult> UpdateNote(
        string id,
        [FromBody] UpdateSavedTraceNoteRequest request,
        CancellationToken ct)
    {
        var updated = await _savedTraceRepository.UpdateNoteAsync(id, request.Note, ct);
        if (!updated)
            return NotFound(new { Message = $"Saved trace '{id}' not found" });

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteTrace(string id, CancellationToken ct)
    {
        var deleted = await _savedTraceRepository.DeleteAsync(id, ct);
        if (!deleted)
            return NotFound(new { Message = $"Saved trace '{id}' not found" });

        return NoContent();
    }

    private static SavedTrace ToEntity(SavedTraceDto dto)
    {
        var trace = new SavedTrace
        {
            Method = dto.Method,
            Url = dto.Url,
            StatusCode = dto.StatusCode,
            DurationMs = dto.DurationMs,
            Timestamp = dto.Timestamp,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note,
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
            trace.WebSocketFrames = dto.WebSocketFrames.Select(f => new SavedTraceWebSocketFrame
            {
                SavedTraceId = trace.Id,
                Timestamp = f.Timestamp,
                Direction = f.Direction,
                FrameType = f.FrameType,
                Payload = f.Payload
            }).ToList();
        }

        return trace;
    }

    private static SavedTraceDto ToDto(SavedTrace trace) => new()
    {
        Id = trace.Id,
        SavedAt = trace.SavedAt,
        Note = trace.Note,
        Method = trace.Method,
        Url = trace.Url,
        StatusCode = trace.StatusCode,
        DurationMs = trace.DurationMs,
        Timestamp = trace.Timestamp,
        RequestHeaders = DeserializeHeaders(trace.RequestHeaders),
        ResponseHeaders = DeserializeHeaders(trace.ResponseHeaders),
        RequestBody = trace.RequestBody,
        ResponseBody = trace.ResponseBody,
        ResponseBodyBase64 = trace.ResponseBodyBase64,
        ResponseContentType = trace.ResponseContentType,
        IsWebSocket = trace.IsWebSocket,
        WebSocketClosed = trace.WebSocketClosed,
        WebSocketFrames = trace.WebSocketFrames.Count > 0
            ? trace.WebSocketFrames
                .OrderBy(f => f.Timestamp)
                .Select(f => new WebSocketFrameDto
                {
                    Timestamp = f.Timestamp,
                    Direction = f.Direction,
                    FrameType = f.FrameType,
                    Payload = f.Payload
                }).ToList()
            : null,
        TimingConnectMs = trace.TimingConnectMs,
        TimingTlsMs = trace.TimingTlsMs,
        TimingSendMs = trace.TimingSendMs,
        TimingWaitMs = trace.TimingWaitMs,
        TimingReceiveMs = trace.TimingReceiveMs,
        TimingConnectionReused = trace.TimingConnectionReused
    };

    private static SavedTraceSummaryDto ToSummary(SavedTrace trace) => new()
    {
        Id = trace.Id,
        Method = trace.Method,
        Url = trace.Url,
        StatusCode = trace.StatusCode,
        DurationMs = trace.DurationMs,
        Timestamp = trace.Timestamp,
        SavedAt = trace.SavedAt,
        Note = trace.Note,
        IsWebSocket = trace.IsWebSocket,
        ResponseBodySize = trace.ResponseBodyBase64 != null
            ? trace.ResponseBodyBase64.Length * 3L / 4L
            : trace.ResponseBody?.Length
    };

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
        return JsonSerializer.Deserialize<List<KeyValuePair<string, string>>>(json);
    }
}
