using shmoxy.models;
using shmoxy.models.dto;

namespace shmoxy.server.interfaces;

/// <summary>
/// Interface for intercepting requests and responses through the proxy.
/// Implement this interface to add custom logic like decoding, filtering, or logging.
/// </summary>
public interface IInterceptHook
{
    /// <summary>
    /// Called when a request is intercepted before forwarding to the target server.
    /// Return null or Cancel=true to stop processing.
    /// </summary>
    Task<InterceptedRequest?> OnRequestAsync(InterceptedRequest request);

    /// <summary>
    /// Called when a response is intercepted after receiving it from the target server.
    /// Return null or Cancel=true to stop processing.
    /// </summary>
    Task<InterceptedResponse?> OnResponseAsync(InterceptedResponse response);

    /// <summary>
    /// Called when a CONNECT request is tunneled via TLS passthrough (no MITM).
    /// </summary>
    Task OnPassthroughAsync(string host, int port) => Task.CompletedTask;

    /// <summary>
    /// Called when a WebSocket connection is opened after a successful 101 upgrade.
    /// </summary>
    Task OnWebSocketOpenAsync(string host, string path, string correlationId) => Task.CompletedTask;

    /// <summary>
    /// Called for each WebSocket frame relayed in either direction.
    /// </summary>
    Task OnWebSocketFrameAsync(string correlationId, WebSocketFrame frame, string direction) => Task.CompletedTask;

    /// <summary>
    /// Called when a WebSocket connection is closed.
    /// </summary>
    Task OnWebSocketCloseAsync(string correlationId, string? reason) => Task.CompletedTask;
}
