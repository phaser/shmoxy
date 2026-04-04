using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using shmoxy.shared.ipc;

namespace shmoxy.ipc;

/// <summary>
/// Minimal API endpoints for IPC control over Unix Domain Sockets.
/// </summary>
public static class ProxyControlApi
{
    public static IEndpointRouteBuilder MapProxyControlApi(this IEndpointRouteBuilder endpoints, ProxyStateService stateService, ProxyConfig config, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("shmoxy.ipc.ProxyControlApi");

        endpoints.MapGet("/ipc/status", () =>
        {
            return Results.Json(new ProxyStatus
            {
                IsListening = stateService.IsListening,
                Port = stateService.Port,
                Uptime = stateService.Uptime,
                ActiveConnections = stateService.ActiveConnections,
                Version = System.Reflection.CustomAttributeExtensions
                    .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(ProxyControlApi).Assembly)?.InformationalVersion
                    ?? typeof(ProxyControlApi).Assembly.GetName().Version?.ToString(3)
            });
        });

        endpoints.MapPost("/ipc/shutdown", async (IHostApplicationLifetime lifetime) =>
        {
            var requestedAt = DateTime.UtcNow;
            logger.LogInformation(
                "Shutdown requested via IPC at {ShutdownRequestedAt}",
                requestedAt);
            lifetime.StopApplication();
            return Results.Json(new ShutdownResponse { Success = true, Message = $"Shutdown initiated at {requestedAt:O}" });
        });

        endpoints.MapGet("/ipc/config", () =>
        {
            return Results.Json(config);
        });

        endpoints.MapPut("/ipc/config", (ProxyConfig newConfig) =>
        {
            // Update all config fields
            config.Port = newConfig.Port;
            config.CertPath = newConfig.CertPath;
            config.KeyPath = newConfig.KeyPath;
            config.LogLevel = newConfig.LogLevel;
            config.PassthroughHosts = newConfig.PassthroughHosts;

            // Sync session logging setting
            config.SessionLoggingEnabled = newConfig.SessionLoggingEnabled;
            if (stateService.SessionLogBuffer != null)
                stateService.SessionLogBuffer.Enabled = config.SessionLoggingEnabled;

            return Results.Json(config);
        });

        endpoints.MapGet("/ipc/hooks", () =>
        {
            var hooks = new List<HookDescriptor>();

            // Add inspection hook if available
            if (stateService.InspectionHook != null)
            {
                hooks.Add(new HookDescriptor
                {
                    Id = "inspection",
                    Name = "Request/Response Inspection",
                    Type = "builtin",
                    Enabled = stateService.InspectionHook.Enabled
                });
            }

            return Results.Json(hooks);
        });

        endpoints.MapPost("/ipc/hooks/{id}/enable", (string id) =>
        {
            if (id == "inspection" && stateService.InspectionHook != null)
            {
                stateService.InspectionHook.Enabled = true;
                return Results.Json(new EnableHookResponse { Success = true, Message = "Hook enabled" });
            }
            return Results.Json(new EnableHookResponse { Success = false, Message = $"Unknown hook: {id}" });
        });

        endpoints.MapPost("/ipc/hooks/{id}/disable", (string id) =>
        {
            if (id == "inspection" && stateService.InspectionHook != null)
            {
                stateService.InspectionHook.Enabled = false;
                return Results.Json(new DisableHookResponse { Success = true, Message = "Hook disabled" });
            }
            return Results.Json(new DisableHookResponse { Success = false, Message = $"Unknown hook: {id}" });
        });

        endpoints.MapGet("/ipc/certs/root.pem", () =>
        {
            try
            {
                var pem = stateService.GetRootCertificatePem();
                return Results.Text(pem, "application/x-pem-file");
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        endpoints.MapGet("/ipc/certs/root.der", () =>
        {
            try
            {
                var der = stateService.GetRootCertificateDer();
                return Results.File(der, "application/x-x509-ca-cert", "shmoxy-root-ca.der");
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        endpoints.MapGet("/ipc/certs/root.pfx", () =>
        {
            try
            {
                var pfx = stateService.GetRootCertificatePfx();
                return Results.File(pfx, "application/x-pkcs12", "shmoxy-root-ca.pfx");
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        endpoints.MapGet("/ipc/inspect/stream", async (HttpResponse response) =>
        {
            if (stateService.InspectionHook == null)
            {
                response.StatusCode = 400;
                await response.WriteAsync("Inspection not available");
                return;
            }

            response.Headers.ContentType = "text/event-stream";

            var reader = stateService.InspectionHook.GetReader();
            var cts = new CancellationTokenSource();

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var hasData = await reader.WaitToReadAsync(cts.Token);
                    if (!hasData) break;

                    while (reader.TryRead(out var evt))
                    {
                        var json = JsonSerializer.Serialize(evt);
                        await response.WriteAsync($"data: {json}\n\n", cts.Token);
                        await response.Body.FlushAsync(cts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on disconnect
            }
        });


        endpoints.MapPost("/ipc/inspect/enable", () =>
        {
            var success = stateService.EnableInspection();
            return Results.Json(new EnableInspectionResponse
            {
                Success = success,
                Message = success ? "Inspection enabled" : "Inspection not available"
            });
        });

        endpoints.MapPost("/ipc/inspect/disable", () =>
        {
            var success = stateService.DisableInspection();
            return Results.Json(new DisableInspectionResponse
            {
                Success = success,
                Message = success ? "Inspection disabled" : "Inspection not available"
            });
        });

        endpoints.MapGet("/ipc/session-log", () =>
        {
            if (stateService.SessionLogBuffer == null)
                return Results.Json(Array.Empty<SessionLogEntry>());

            return Results.Json(stateService.SessionLogBuffer.Snapshot());
        });

        endpoints.MapPost("/ipc/session-log/drain", () =>
        {
            if (stateService.SessionLogBuffer == null)
                return Results.Json(Array.Empty<SessionLogEntry>());

            return Results.Json(stateService.SessionLogBuffer.Drain());
        });

        // Breakpoint endpoints
        endpoints.MapPost("/ipc/breakpoints/enable", () =>
        {
            if (stateService.BreakpointHook == null)
                return Results.Json(new { Success = false, Message = "Breakpoints not available" });

            stateService.BreakpointHook.Enabled = true;
            return Results.Json(new { Success = true, Message = "Breakpoints enabled" });
        });

        endpoints.MapPost("/ipc/breakpoints/disable", () =>
        {
            if (stateService.BreakpointHook == null)
                return Results.Json(new { Success = false, Message = "Breakpoints not available" });

            stateService.BreakpointHook.Enabled = false;
            return Results.Json(new { Success = true, Message = "Breakpoints disabled" });
        });

        endpoints.MapGet("/ipc/breakpoints/paused", () =>
        {
            if (stateService.BreakpointHook == null)
                return Results.Json(Array.Empty<object>());

            var paused = stateService.BreakpointHook.GetPausedRequests()
                .Select(p => new
                {
                    p.CorrelationId,
                    p.Request.Method,
                    Url = p.Request.Url?.ToString(),
                    Headers = p.Request.Headers,
                    Body = p.Request.Body != null ? System.Text.Encoding.UTF8.GetString(p.Request.Body) : null,
                    p.PausedAt
                });

            return Results.Json(paused);
        });

        endpoints.MapPost("/ipc/breakpoints/{correlationId}/release", async (string correlationId, HttpRequest request) =>
        {
            if (stateService.BreakpointHook == null)
                return Results.Json(new { Success = false, Message = "Breakpoints not available" });

            // Check if there's a modified request in the body
            shmoxy.models.dto.InterceptedRequest? modified = null;
            if (request.ContentLength > 0)
            {
                modified = await JsonSerializer.DeserializeAsync<shmoxy.models.dto.InterceptedRequest>(
                    request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            var success = stateService.BreakpointHook.Release(correlationId, modified);
            return Results.Json(new { Success = success, Message = success ? "Request released" : "Request not found" });
        });

        endpoints.MapPost("/ipc/breakpoints/{correlationId}/drop", (string correlationId) =>
        {
            if (stateService.BreakpointHook == null)
                return Results.Json(new { Success = false, Message = "Breakpoints not available" });

            var success = stateService.BreakpointHook.Drop(correlationId);
            return Results.Json(new { Success = success, Message = success ? "Request dropped" : "Request not found" });
        });

        // Breakpoint rules
        endpoints.MapGet("/ipc/breakpoints/rules", () =>
        {
            if (stateService.BreakpointHook == null)
                return Results.Json(Array.Empty<object>());

            return Results.Json(stateService.BreakpointHook.GetRules());
        });

        endpoints.MapPost("/ipc/breakpoints/rules", async (HttpRequest request) =>
        {
            if (stateService.BreakpointHook == null)
                return Results.Json(new { Success = false, Message = "Breakpoints not available" });

            var body = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body);
            var method = body.TryGetProperty("method", out var m) ? m.GetString() : null;
            var urlPattern = body.TryGetProperty("urlPattern", out var u) ? u.GetString() ?? "" : "";

            var isRegex = body.TryGetProperty("isRegex", out var r) && r.GetBoolean();
            var rule = stateService.BreakpointHook.AddRule(method, urlPattern, isRegex);
            return Results.Json(rule);
        });

        endpoints.MapDelete("/ipc/breakpoints/rules/{id}", (string id) =>
        {
            if (stateService.BreakpointHook == null)
                return Results.Json(new { Success = false });

            var success = stateService.BreakpointHook.RemoveRule(id);
            return Results.Json(new { Success = success });
        });

        return endpoints;
    }
}
