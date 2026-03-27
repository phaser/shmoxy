namespace shmoxy.api.models;

/// <summary>
/// Identifies what initiated a proxy shutdown.
/// </summary>
public enum ShutdownSource
{
    /// <summary>User clicked stop/restart in the UI or called the API.</summary>
    User,

    /// <summary>Application host is shutting down (e.g., SIGTERM, SIGINT, process exit).</summary>
    System,

    /// <summary>Health check detected the proxy is unresponsive.</summary>
    HealthCheck,

    /// <summary>Object disposal triggered shutdown cleanup.</summary>
    Dispose
}
