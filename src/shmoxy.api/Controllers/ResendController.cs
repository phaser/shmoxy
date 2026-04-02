using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using shmoxy.api.models;
using shmoxy.api.models.dto;
using shmoxy.api.server;

namespace shmoxy.api.Controllers;

[ApiController]
[Route("api/inspection")]
public class ResendController : ControllerBase
{
    private readonly IProxyProcessManager _processManager;
    private readonly ILogger<ResendController> _logger;

    public ResendController(
        IProxyProcessManager processManager,
        ILogger<ResendController> logger)
    {
        _processManager = processManager;
        _logger = logger;
    }

    [HttpPost("resend")]
    public async Task<ActionResult> Resend([FromBody] ResendRequestDto request, CancellationToken ct)
    {
        var state = await _processManager.GetStateAsync();
        if (state?.State != ProxyProcessState.Running || !state.Port.HasValue)
            return BadRequest(new { Error = "Proxy is not running" });

        var proxyPort = state.Port.Value;

        try
        {
            var proxy = new WebProxy($"http://localhost:{proxyPort}");
            using var handler = new HttpClientHandler
            {
                Proxy = proxy,
                UseProxy = true,
                // Accept the proxy's MITM certificate so HTTPS resends go through the
                // proxy tunnel and are captured by the inspection hooks.
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

            using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);

            foreach (var (key, value) in request.Headers)
            {
                // Skip pseudo-headers and content headers that need special handling
                if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;

                httpRequest.Headers.TryAddWithoutValidation(key, value);
            }

            if (!string.IsNullOrEmpty(request.Body))
            {
                var contentType = request.Headers
                    .FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    .Value ?? "application/octet-stream";

                httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, contentType);
            }

            _logger.LogInformation("Resending {Method} {Url} through proxy", request.Method, request.Url);

            var response = await client.SendAsync(httpRequest, ct);

            var responseBytes = await response.Content.ReadAsByteArrayAsync(ct);
            var responseHeaders = response.Headers
                .Concat(response.Content.Headers)
                .GroupBy(h => h.Key)
                .ToDictionary(g => g.Key, g => string.Join(", ", g.SelectMany(h => h.Value)));

            var responseContentType = response.Content.Headers.ContentType?.MediaType;
            var isText = IsTextContentType(responseContentType);

            return Ok(new ResendResponseDto
            {
                StatusCode = (int)response.StatusCode,
                Headers = responseHeaders,
                Body = isText ? Encoding.UTF8.GetString(responseBytes) : Convert.ToBase64String(responseBytes),
                ContentType = responseContentType,
                IsBase64 = !isText
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Resend failed for {Method} {Url}", request.Method, request.Url);
            return StatusCode(502, new { Error = ex.Message });
        }
    }

    private static bool IsTextContentType(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType))
            return true;

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return true;

        return mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("css", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }
}
