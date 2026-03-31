using System.Collections.Concurrent;
using shmoxy.models.dto;
using shmoxy.server.interfaces;

namespace shmoxy.server.hooks;

/// <summary>
/// Hook that can pause requests mid-flight, allowing the user to inspect, edit,
/// and forward them. When enabled, all requests are held until explicitly released.
/// </summary>
public class BreakpointHook : IInterceptHook
{
    private readonly ConcurrentDictionary<string, PausedRequest> _pausedRequests = new();
    private readonly ConcurrentDictionary<string, BreakpointRule> _rules = new();
    private volatile bool _enabled;
    private int _timeoutMs = 60_000;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public int TimeoutMs
    {
        get => _timeoutMs;
        set => _timeoutMs = value;
    }

    public IReadOnlyCollection<PausedRequest> GetPausedRequests() =>
        _pausedRequests.Values.ToList().AsReadOnly();

    public IReadOnlyCollection<BreakpointRule> GetRules() =>
        _rules.Values.ToList().AsReadOnly();

    public BreakpointRule AddRule(string? method, string urlPattern)
    {
        var rule = new BreakpointRule
        {
            Id = Guid.NewGuid().ToString(),
            Method = method,
            UrlPattern = urlPattern
        };
        _rules[rule.Id] = rule;
        // Auto-enable when a rule is added
        _enabled = true;
        return rule;
    }

    public bool RemoveRule(string id) => _rules.TryRemove(id, out _);

    private bool MatchesAnyRule(InterceptedRequest request)
    {
        if (_rules.IsEmpty)
            return true; // No rules = break on all (legacy behavior)

        var url = request.Url?.ToString() ?? "";
        foreach (var rule in _rules.Values)
        {
            if (rule.Method != null &&
                !rule.Method.Equals(request.Method, StringComparison.OrdinalIgnoreCase))
                continue;

            if (url.Contains(rule.UrlPattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async Task<InterceptedRequest?> OnRequestAsync(InterceptedRequest request)
    {
        if (!_enabled || string.IsNullOrEmpty(request.CorrelationId))
            return request;

        if (!MatchesAnyRule(request))
            return request;

        var paused = new PausedRequest
        {
            CorrelationId = request.CorrelationId,
            Request = request,
            PausedAt = DateTime.UtcNow,
            Completion = new TaskCompletionSource<InterceptedRequest?>()
        };

        _pausedRequests[request.CorrelationId] = paused;

        try
        {
            using var cts = new CancellationTokenSource(_timeoutMs);
            cts.Token.Register(() => paused.Completion.TrySetResult(request));

            return await paused.Completion.Task;
        }
        finally
        {
            _pausedRequests.TryRemove(request.CorrelationId, out _);
        }
    }

    public Task<InterceptedResponse?> OnResponseAsync(InterceptedResponse response) =>
        Task.FromResult<InterceptedResponse?>(response);

    /// <summary>
    /// Release a paused request, optionally with modifications.
    /// </summary>
    public bool Release(string correlationId, InterceptedRequest? modified = null)
    {
        if (!_pausedRequests.TryGetValue(correlationId, out var paused))
            return false;

        var result = modified ?? paused.Request;
        return paused.Completion.TrySetResult(result);
    }

    /// <summary>
    /// Drop a paused request (cancel it).
    /// </summary>
    public bool Drop(string correlationId)
    {
        if (!_pausedRequests.TryGetValue(correlationId, out var paused))
            return false;

        return paused.Completion.TrySetResult(null);
    }

    public class PausedRequest
    {
        public string CorrelationId { get; init; } = string.Empty;
        public InterceptedRequest Request { get; init; } = new();
        public DateTime PausedAt { get; init; }
        internal TaskCompletionSource<InterceptedRequest?> Completion { get; init; } = new();
    }

    public class BreakpointRule
    {
        public string Id { get; init; } = string.Empty;
        public string? Method { get; init; }
        public string UrlPattern { get; init; } = string.Empty;
    }
}
