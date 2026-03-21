using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace shmoxy.ipc;

/// <summary>
/// Holds the API key for admin authentication.
/// </summary>
public class ApiKeyService
{
    public string? ApiKey { get; set; }
}

/// <summary>
/// Middleware that validates API key authentication for HTTP requests.
/// Unix socket connections bypass authentication.
/// </summary>
public class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiKeyService _apiKeyService;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, ApiKeyService apiKeyService)
    {
        _next = next;
        _apiKeyService = apiKeyService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresAuthentication(context))
        {
            var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();

            if (string.IsNullOrEmpty(apiKey) || apiKey != _apiKeyService.ApiKey)
            {
                context.Response.StatusCode = 401;
                context.Response.Headers.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\": \"Unauthorized. Provide X-API-Key header.\"}");
                return;
            }
        }

        await _next(context);
    }

    private static bool RequiresAuthentication(HttpContext context)
    {
        var connectionFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpConnectionFeature>();
        if (connectionFeature == null) return true;

        var localEndPoint = connectionFeature.LocalIpAddress;
        var remoteEndPoint = connectionFeature.RemoteIpAddress;

        if (localEndPoint == null && remoteEndPoint == null)
        {
            return false;
        }

        return true;
    }
}
