using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.api.Controllers;

[ApiController]
[Route("api/proxies/{proxyId}/detectors")]
public class DetectorsController : ControllerBase
{
    private readonly IProxyProcessManager _processManager;
    private readonly ILogger<DetectorsController> _logger;

    public DetectorsController(IProxyProcessManager processManager, ILogger<DetectorsController> logger)
    {
        _processManager = processManager;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DetectorDescriptor>>> GetDetectors(
        string proxyId, CancellationToken ct)
    {
        try
        {
            var state = await _processManager.GetStateAsync();
            if (state?.State != ProxyProcessState.Running)
                return BadRequest(new { Message = "Proxy must be running" });

            var detectors = await _processManager.GetIpcClient().GetDetectorsAsync(ct);
            return Ok(detectors);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("{detectorId}/enable")]
    public async Task<IActionResult> EnableDetector(
        string proxyId, string detectorId, CancellationToken ct)
    {
        try
        {
            var state = await _processManager.GetStateAsync();
            if (state?.State != ProxyProcessState.Running)
                return BadRequest(new { Message = "Proxy must be running" });

            await _processManager.GetIpcClient().EnableDetectorAsync(detectorId, ct);
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("{detectorId}/disable")]
    public async Task<IActionResult> DisableDetector(
        string proxyId, string detectorId, CancellationToken ct)
    {
        try
        {
            var state = await _processManager.GetStateAsync();
            if (state?.State != ProxyProcessState.Running)
                return BadRequest(new { Message = "Proxy must be running" });

            await _processManager.GetIpcClient().DisableDetectorAsync(detectorId, ct);
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpGet("suggestions")]
    public async Task<IActionResult> GetSuggestions(string proxyId, CancellationToken ct)
    {
        try
        {
            var state = await _processManager.GetStateAsync();
            if (state?.State != ProxyProcessState.Running)
                return BadRequest(new { Message = "Proxy must be running" });

            var suggestions = await _processManager.GetIpcClient().GetSuggestionsAsync(ct);
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("suggestions/accept")]
    public async Task<IActionResult> AcceptSuggestion(
        string proxyId, [FromBody] AcceptSuggestionRequest request, CancellationToken ct)
    {
        try
        {
            var state = await _processManager.GetStateAsync();
            if (state?.State != ProxyProcessState.Running)
                return BadRequest(new { Message = "Proxy must be running" });

            await _processManager.GetIpcClient().AcceptSuggestionAsync(request.Host, ct);
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("suggestions/dismiss")]
    public async Task<IActionResult> DismissSuggestion(
        string proxyId, [FromBody] DismissSuggestionRequest request, CancellationToken ct)
    {
        try
        {
            var state = await _processManager.GetStateAsync();
            if (state?.State != ProxyProcessState.Running)
                return BadRequest(new { Message = "Proxy must be running" });

            await _processManager.GetIpcClient().DismissSuggestionAsync(request.Host, ct);
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpGet("temp-passthrough")]
    public async Task<IActionResult> GetTempPassthrough(string proxyId, CancellationToken ct)
    {
        try
        {
            var state = await _processManager.GetStateAsync();
            if (state?.State != ProxyProcessState.Running)
                return BadRequest(new { Message = "Proxy must be running" });

            var entries = await _processManager.GetIpcClient().GetTempPassthroughAsync(ct);
            return Ok(entries);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}
