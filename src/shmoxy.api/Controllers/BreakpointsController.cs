using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using shmoxy.api.models;
using shmoxy.api.server;

namespace shmoxy.api.Controllers;

[ApiController]
[Route("api/breakpoints")]
public class BreakpointsController : ControllerBase
{
    private readonly IProxyProcessManager _processManager;
    private readonly ILogger<BreakpointsController> _logger;

    public BreakpointsController(
        IProxyProcessManager processManager,
        ILogger<BreakpointsController> logger)
    {
        _processManager = processManager;
        _logger = logger;
    }

    [HttpPost("enable")]
    public async Task<ActionResult> Enable(CancellationToken ct)
    {
        var client = _processManager.GetIpcClient();
        await client.EnableBreakpointsAsync(ct);
        return Ok(new { Enabled = true });
    }

    [HttpPost("disable")]
    public async Task<ActionResult> Disable(CancellationToken ct)
    {
        var client = _processManager.GetIpcClient();
        await client.DisableBreakpointsAsync(ct);
        return Ok(new { Enabled = false });
    }

    [HttpGet("paused")]
    public async Task<ActionResult> GetPaused(CancellationToken ct)
    {
        var client = _processManager.GetIpcClient();
        var json = await client.GetPausedRequestsAsync(ct);
        return Content(json, "application/json");
    }

    [HttpPost("paused/{correlationId}/release")]
    public async Task<ActionResult> Release(string correlationId, CancellationToken ct)
    {
        var client = _processManager.GetIpcClient();
        string? body = null;
        if (Request.ContentLength > 0)
        {
            using var reader = new StreamReader(Request.Body);
            body = await reader.ReadToEndAsync(ct);
        }
        await client.ReleaseRequestAsync(correlationId, body, ct);
        return Ok(new { Released = true });
    }

    [HttpPost("paused/{correlationId}/drop")]
    public async Task<ActionResult> Drop(string correlationId, CancellationToken ct)
    {
        var client = _processManager.GetIpcClient();
        await client.DropRequestAsync(correlationId, ct);
        return Ok(new { Dropped = true });
    }

    [HttpGet("rules")]
    public async Task<ActionResult> GetRules(CancellationToken ct)
    {
        var client = _processManager.GetIpcClient();
        var json = await client.GetBreakpointRulesAsync(ct);
        return Content(json, "application/json");
    }

    [HttpPost("rules")]
    public async Task<ActionResult> AddRule([FromBody] AddBreakpointRuleRequest request, CancellationToken ct)
    {
        var client = _processManager.GetIpcClient();
        var json = await client.AddBreakpointRuleAsync(request.Method, request.UrlPattern, request.IsRegex, ct);
        return Content(json, "application/json");
    }

    [HttpDelete("rules/{id}")]
    public async Task<ActionResult> RemoveRule(string id, CancellationToken ct)
    {
        var client = _processManager.GetIpcClient();
        await client.RemoveBreakpointRuleAsync(id, ct);
        return Ok(new { Removed = true });
    }
}

public class AddBreakpointRuleRequest
{
    public string? Method { get; set; }
    public string UrlPattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
}
