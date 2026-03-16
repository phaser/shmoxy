namespace shmoxy;

/// <summary>
/// Represents a request intercepted by the proxy.
/// </summary>
public class InterceptedRequest
{
    public string Method { get; set; } = string.Empty;
    public Uri? Url { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 80;
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public byte[]? Body { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to cancel the request.
    /// </summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Represents a response intercepted by the proxy.
/// </summary>
public class InterceptedResponse
{
    public int StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public byte[] Body { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets a value indicating whether to cancel/replace the response.
    /// </summary>
    public bool Cancel { get; set; }
}

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
}

/// <summary>
/// Default no-op implementation of IInterceptHook.
/// </summary>
public class NoOpInterceptHook : IInterceptHook
{
    public virtual Task<InterceptedRequest?> OnRequestAsync(InterceptedRequest request) =>
        Task.FromResult<InterceptedRequest?>(request);

    public virtual Task<InterceptedResponse?> OnResponseAsync(InterceptedResponse response) =>
        Task.FromResult<InterceptedResponse?>(response);
}

/// <summary>
/// Middleware chain for intercepting requests and responses.
/// Allows multiple hooks to be chained together.
/// </summary>
public class InterceptHookChain : IInterceptHook, IDisposable
{
    private readonly List<IInterceptHook> _hooks = new();
    private bool _disposed;

    /// <summary>
    /// Adds a hook to the chain. Hooks are executed in order of addition.
    /// </summary>
    public InterceptHookChain Add(IInterceptHook hook)
    {
        if (hook == null) throw new ArgumentNullException(nameof(hook));
        _hooks.Add(hook);
        return this;
    }

    /// <summary>
    /// Called when a request is intercepted. All hooks are executed in sequence.
    /// </summary>
    public async Task<InterceptedRequest?> OnRequestAsync(InterceptedRequest request)
    {
        foreach (var hook in _hooks)
        {
            var result = await hook.OnRequestAsync(request);
            if (result == null || result.Cancel)
                return result;

            request = result;
        }
        return request;
    }

    /// <summary>
    /// Called when a response is intercepted. All hooks are executed in sequence.
    /// </summary>
    public async Task<InterceptedResponse?> OnResponseAsync(InterceptedResponse response)
    {
        foreach (var hook in _hooks)
        {
            var result = await hook.OnResponseAsync(response);
            if (result == null || result.Cancel)
                return result;

            response = result;
        }
        return response;
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var hook in _hooks.OfType<IDisposable>())
            hook.Dispose();

        _disposed = true;
    }
}
