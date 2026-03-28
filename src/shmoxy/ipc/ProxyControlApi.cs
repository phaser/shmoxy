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
                Version = typeof(ProxyControlApi).Assembly.GetName().Version?.ToString(3)
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
            // Update runtime config (log level, passthrough hosts, detectors, etc.)
            if (newConfig.LogLevel != config.LogLevel)
            {
                config.LogLevel = newConfig.LogLevel;
            }
            config.PassthroughHosts = newConfig.PassthroughHosts;

            // Sync enabled detectors
            config.EnabledDetectors = newConfig.EnabledDetectors;
            if (stateService.DetectorHook != null)
            {
                // Disable all first, then enable from config
                foreach (var d in stateService.DetectorHook.GetDetectors())
                    stateService.DetectorHook.SetDetectorEnabled(d.Id, false);
                stateService.DetectorHook.EnableDetectors(config.EnabledDetectors);
            }

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

        endpoints.MapGet("/ipc/detectors", () =>
        {
            if (stateService.DetectorHook == null)
                return Results.Json(Array.Empty<DetectorDescriptor>());

            return Results.Json(stateService.DetectorHook.GetDetectors());
        });

        endpoints.MapPost("/ipc/detectors/{id}/enable", (string id) =>
        {
            if (stateService.DetectorHook == null)
                return Results.Json(new { Success = false, Message = "Detectors not available" });

            var success = stateService.DetectorHook.SetDetectorEnabled(id, true);
            if (success && !config.EnabledDetectors.Contains(id))
                config.EnabledDetectors.Add(id);

            return Results.Json(new { Success = success, Message = success ? "Detector enabled" : $"Unknown detector: {id}" });
        });

        endpoints.MapPost("/ipc/detectors/{id}/disable", (string id) =>
        {
            if (stateService.DetectorHook == null)
                return Results.Json(new { Success = false, Message = "Detectors not available" });

            var success = stateService.DetectorHook.SetDetectorEnabled(id, false);
            if (success)
                config.EnabledDetectors.Remove(id);

            return Results.Json(new { Success = success, Message = success ? "Detector disabled" : $"Unknown detector: {id}" });
        });

        endpoints.MapGet("/ipc/detectors/suggestions", () =>
        {
            if (stateService.DetectorHook == null)
                return Results.Json(Array.Empty<PassthroughSuggestion>());

            return Results.Json(stateService.DetectorHook.GetSuggestions());
        });

        endpoints.MapGet("/ipc/detectors/suggestions/stream", async (HttpResponse response) =>
        {
            if (stateService.DetectorHook == null)
            {
                response.StatusCode = 400;
                await response.WriteAsync("Detectors not available");
                return;
            }

            response.Headers.ContentType = "text/event-stream";

            var reader = stateService.DetectorHook.GetSuggestionReader();
            var cts = new CancellationTokenSource();

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var hasData = await reader.WaitToReadAsync(cts.Token);
                    if (!hasData) break;

                    while (reader.TryRead(out var suggestion))
                    {
                        var json = JsonSerializer.Serialize(suggestion);
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

        endpoints.MapGet("/ipc/detectors/temp-passthrough", () =>
        {
            if (stateService.TempPassthrough == null)
                return Results.Json(Array.Empty<TemporaryPassthroughEntry>());

            return Results.Json(stateService.TempPassthrough.GetActiveEntries());
        });

        endpoints.MapPost("/ipc/detectors/suggestions/dismiss", (DismissSuggestionRequest req) =>
        {
            if (stateService.DetectorHook == null)
                return Results.Json(new { Success = false, Message = "Detectors not available" });

            stateService.DetectorHook.DismissSuggestion(req.Host);
            return Results.Json(new { Success = true, Message = $"Dismissed suggestions for {req.Host}" });
        });

        endpoints.MapPost("/ipc/detectors/suggestions/accept", (AcceptSuggestionRequest req) =>
        {
            if (stateService.DetectorHook == null)
                return Results.Json(new { Success = false, Message = "Detectors not available" });

            // Add to passthrough hosts
            if (!config.PassthroughHosts.Contains(req.Host))
                config.PassthroughHosts.Add(req.Host);

            // Dismiss so it doesn't re-suggest
            stateService.DetectorHook.DismissSuggestion(req.Host);

            return Results.Json(new { Success = true, Message = $"Added {req.Host} to passthrough list" });
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

        return endpoints;
    }
}
