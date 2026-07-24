using shmoxy.models.dto;
using shmoxy.server.interfaces;

namespace shmoxy.server.hooks;

/// <summary>
/// Default no-op implementation of IInterceptHook.
/// </summary>
public class NoOpInterceptHook : IInterceptHook
{
    public virtual bool RequiresRequestBodyCapture(InterceptedRequest request) => false;

    public virtual bool RequiresResponseBodyCapture(InterceptedResponse response) => false;

    public virtual Task<InterceptedRequest?> OnRequestAsync(InterceptedRequest request) =>
        Task.FromResult<InterceptedRequest?>(request);

    public virtual Task<InterceptedResponse?> OnResponseAsync(InterceptedResponse response) =>
        Task.FromResult<InterceptedResponse?>(response);
}
