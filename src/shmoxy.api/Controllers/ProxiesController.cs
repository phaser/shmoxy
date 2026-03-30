using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using shmoxy.api.models;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.api.Controllers;

[ApiController]
[Route("api/proxies/local")]
public class ProxiesController : ControllerBase
{
    private readonly ILogger<ProxiesController> _logger;
    private readonly IProxyProcessManager _processManager;

    public ProxiesController(
        ILogger<ProxiesController> logger,
        IProxyProcessManager processManager)
    {
        _logger = logger;
        _processManager = processManager;
    }

    /// <summary>
    /// Get the current state of the local proxy.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ProxyInstanceState>> GetProxyState(CancellationToken ct)
    {
        var state = await _processManager.GetStateAsync();
        if (state == null)
        {
            return NotFound(new { Message = "Local proxy not found" });
        }

        return Ok(state);
    }

    /// <summary>
    /// Start the local proxy.
    /// </summary>
    [HttpPost("start")]
    public async Task<ActionResult<ProxyInstanceState>> StartProxy(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Starting local proxy");
            var state = await _processManager.StartAsync(ct);
            _logger.LogInformation("Local proxy started with state {State}", state.State);
            return Ok(state);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to start proxy");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error starting proxy");
            return StatusCode(500, new { Message = "Failed to start proxy", Error = ex.Message });
        }
    }

    /// <summary>
    /// Stop the local proxy.
    /// </summary>
    [HttpPost("stop")]
    public async Task<ActionResult> StopProxy(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Stopping local proxy (user request)");
            await _processManager.StopAsync(ShutdownSource.User, ct);
            _logger.LogInformation("Local proxy stopped");
            return Ok(new { Message = "Proxy stopped successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping proxy");
            return StatusCode(500, new { Message = "Failed to stop proxy", Error = ex.Message });
        }
    }

    /// <summary>
    /// Restart the local proxy.
    /// </summary>
    [HttpPost("restart")]
    public async Task<ActionResult<ProxyInstanceState>> RestartProxy(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Restarting local proxy (user request)");
            await _processManager.StopAsync(ShutdownSource.User, ct);
            var state = await _processManager.StartAsync(ct);
            _logger.LogInformation("Local proxy restarted with state {State}", state.State);
            return Ok(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting proxy");
            return StatusCode(500, new { Message = "Failed to restart proxy", Error = ex.Message });
        }
    }

    /// <summary>
    /// Get active temporary passthrough entries from the proxy.
    /// </summary>
    [HttpGet("temp-passthrough")]
    public async Task<ActionResult<IReadOnlyList<TemporaryPassthroughEntry>>> GetTempPassthrough(CancellationToken ct)
    {
        var state = await _processManager.GetStateAsync();
        if (state?.State != ProxyProcessState.Running)
            return Ok(Array.Empty<TemporaryPassthroughEntry>());

        try
        {
            var client = _processManager.GetIpcClient();
            var entries = await client.GetTempPassthroughAsync(ct);
            return Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get temp passthrough entries from proxy");
            return Ok(Array.Empty<TemporaryPassthroughEntry>());
        }
    }

    /// <summary>
    /// Drain the session log buffer from the proxy. Returns all buffered entries and clears the buffer.
    /// </summary>
    [HttpPost("session-log/drain")]
    public async Task<ActionResult<IReadOnlyList<SessionLogEntry>>> DrainSessionLog(CancellationToken ct)
    {
        var state = await _processManager.GetStateAsync();
        if (state?.State != ProxyProcessState.Running)
            return Ok(Array.Empty<SessionLogEntry>());

        try
        {
            var client = _processManager.GetIpcClient();
            var entries = await client.DrainSessionLogAsync(ct);
            return Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to drain session log from proxy");
            return Ok(Array.Empty<SessionLogEntry>());
        }
    }
}
