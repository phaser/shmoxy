using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using shmoxy.api.models;
using shmoxy.api.models.dto;
using shmoxy.api.server;

namespace shmoxy.api.Controllers;

[ApiController]
[Route("api/proxies/remote")]
public class RemoteProxiesController : ControllerBase
{
    private readonly ILogger<RemoteProxiesController> _logger;
    private readonly IRemoteProxyRegistry _registry;

    public RemoteProxiesController(
        ILogger<RemoteProxiesController> logger,
        IRemoteProxyRegistry registry)
    {
        _logger = logger;
        _registry = registry;
    }

    /// <summary>
    /// List all remote proxies.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RemoteProxyResponse>>> GetAllProxies(CancellationToken ct)
    {
        var proxies = await _registry.GetAllAsync(ct);
        var responses = proxies.Select(p => ToResponse(p)).ToList();
        return Ok(responses);
    }

    /// <summary>
    /// Get a remote proxy by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<RemoteProxyResponse>> GetProxy(string id, CancellationToken ct)
    {
        var proxy = await _registry.GetByIdAsync(id, ct);
        if (proxy == null)
        {
            return NotFound(new { Message = $"Remote proxy {id} not found" });
        }

        return Ok(ToResponse(proxy));
    }

    /// <summary>
    /// Register a new remote proxy.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<RemoteProxyResponse>> RegisterProxy(
        [FromBody] RegisterRemoteProxyRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.Name))
        {
            return BadRequest(new { Message = "Name is required" });
        }

        if (string.IsNullOrEmpty(request.AdminUrl))
        {
            return BadRequest(new { Message = "AdminUrl is required" });
        }

        if (!Uri.TryCreate(request.AdminUrl, UriKind.Absolute, out _))
        {
            return BadRequest(new { Message = "AdminUrl must be a valid absolute URL" });
        }

        if (string.IsNullOrEmpty(request.ApiKey))
        {
            return BadRequest(new { Message = "ApiKey is required" });
        }

        // Test connectivity before registering
        _logger.LogInformation("Testing connectivity to {Url}", request.AdminUrl);
        var isConnected = await _registry.TestConnectivityAsync(request.AdminUrl, request.ApiKey, ct);
        if (!isConnected)
        {
            return BadRequest(new { Message = "Failed to connect to remote proxy. Verify URL and API key." });
        }

        var proxy = new RemoteProxy
        {
            Name = request.Name,
            AdminUrl = request.AdminUrl,
            ApiKey = request.ApiKey
        };

        var registered = await _registry.RegisterAsync(proxy, ct);
        _logger.LogInformation("Registered remote proxy {Name} at {Url}", registered.Name, registered.AdminUrl);

        return CreatedAtAction(nameof(GetProxy), new { id = registered.Id }, ToResponse(registered));
    }

    /// <summary>
    /// Update a remote proxy (API key rotation).
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<RemoteProxyResponse>> UpdateProxy(
        string id,
        [FromBody] UpdateRemoteProxyRequest request,
        CancellationToken ct)
    {
        var existing = await _registry.GetByIdAsync(id, ct);
        if (existing == null)
        {
            return NotFound(new { Message = $"Remote proxy {id} not found" });
        }

        if (!string.IsNullOrEmpty(request.ApiKey))
        {
            existing.ApiKey = request.ApiKey;
        }

        var updated = await _registry.UpdateAsync(existing, ct);
        return Ok(ToResponse(updated!));
    }

    /// <summary>
    /// Unregister a remote proxy.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> UnregisterProxy(string id, CancellationToken ct)
    {
        var deleted = await _registry.UnregisterAsync(id, ct);
        if (!deleted)
        {
            return NotFound(new { Message = $"Remote proxy {id} not found" });
        }

        return NoContent();
    }

    /// <summary>
    /// Force an immediate health check.
    /// </summary>
    [HttpPost("{id}/health")]
    public async Task<ActionResult<RemoteProxyResponse>> ForceHealthCheck(string id, CancellationToken ct)
    {
        var proxy = await _registry.GetByIdAsync(id, ct);
        if (proxy == null)
        {
            return NotFound(new { Message = $"Remote proxy {id} not found" });
        }

        var isHealthy = await _registry.TestConnectivityAsync(proxy.AdminUrl, proxy.ApiKey, ct);
        proxy.Status = isHealthy ? RemoteProxyStatus.Healthy : RemoteProxyStatus.Unhealthy;
        proxy.LastHealthCheck = DateTime.UtcNow;
        await _registry.UpdateAsync(proxy, ct);

        return Ok(ToResponse(proxy));
    }

    private static RemoteProxyResponse ToResponse(RemoteProxy proxy)
    {
        return new RemoteProxyResponse
        {
            Id = proxy.Id,
            Name = proxy.Name,
            AdminUrl = proxy.AdminUrl,
            Status = proxy.Status.ToString(),
            LastHealthCheck = proxy.LastHealthCheck,
            CreatedAt = proxy.CreatedAt,
            UpdatedAt = proxy.UpdatedAt
        };
    }
}
