using shmoxy.models.dto;
using shmoxy.server.interfaces;

namespace shmoxy.server.hooks;

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
            if (result == null)
                return null;

            if (result.Cancel)
                return null;

            if (!ReferenceEquals(result, request))
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

    /// <summary>
    /// Called when a CONNECT request is tunneled via TLS passthrough.
    /// All hooks are notified in sequence.
    /// </summary>
    public async Task OnPassthroughAsync(string host, int port)
    {
        foreach (var hook in _hooks)
        {
            await hook.OnPassthroughAsync(host, port);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var hook in _hooks.OfType<IDisposable>())
            hook.Dispose();

        _disposed = true;
    }
}
