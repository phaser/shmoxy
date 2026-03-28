using System.Collections.Concurrent;
using System.Threading.Channels;
using shmoxy.models.dto;
using shmoxy.server.interfaces;
using shmoxy.shared.ipc;

namespace shmoxy.server.hooks;

/// <summary>
/// Hook that analyzes intercepted request/response pairs using pluggable detectors
/// to suggest domains that should be added to the TLS passthrough list.
/// </summary>
public class PassthroughDetectorHook : IInterceptHook, IDisposable
{
    private readonly List<IPassthroughDetector> _detectors = new();
    private readonly Dictionary<string, bool> _enabledDetectors = new();
    private readonly ConcurrentDictionary<string, InterceptedRequest> _pendingRequests = new();
    private readonly Channel<PassthroughSuggestion> _suggestions;
    private readonly List<PassthroughSuggestion> _activeSuggestions = new();
    private readonly HashSet<string> _dismissedHosts = new();
    private readonly HashSet<string> _suggestedHosts = new();
    private readonly object _lock = new();
    private bool _disposed;

    public PassthroughDetectorHook()
    {
        _suggestions = Channel.CreateUnbounded<PassthroughSuggestion>();
    }

    /// <summary>
    /// Registers a detector. All detectors start enabled by default.
    /// </summary>
    public PassthroughDetectorHook AddDetector(IPassthroughDetector detector)
    {
        _detectors.Add(detector);
        _enabledDetectors[detector.Id] = true;
        return this;
    }

    /// <summary>
    /// Gets the list of registered detectors and their enabled state.
    /// </summary>
    public IReadOnlyList<DetectorDescriptor> GetDetectors()
    {
        return _detectors.Select(d => new DetectorDescriptor
        {
            Id = d.Id,
            Name = d.Name,
            Enabled = _enabledDetectors.GetValueOrDefault(d.Id, false)
        }).ToList();
    }

    /// <summary>
    /// Enables or disables a specific detector by ID.
    /// </summary>
    public bool SetDetectorEnabled(string detectorId, bool enabled)
    {
        if (!_enabledDetectors.ContainsKey(detectorId))
            return false;

        _enabledDetectors[detectorId] = enabled;
        return true;
    }

    /// <summary>
    /// Enables detectors by their IDs (from config).
    /// </summary>
    public void EnableDetectors(IEnumerable<string> detectorIds)
    {
        foreach (var id in detectorIds)
        {
            if (_enabledDetectors.ContainsKey(id))
                _enabledDetectors[id] = true;
        }
    }

    /// <summary>
    /// Gets the channel reader for consuming passthrough suggestions.
    /// </summary>
    public ChannelReader<PassthroughSuggestion> GetSuggestionReader() => _suggestions.Reader;

    /// <summary>
    /// Dismisses a suggestion so it won't resurface.
    /// </summary>
    /// <summary>
    /// Gets all active (non-dismissed) suggestions.
    /// </summary>
    public IReadOnlyList<PassthroughSuggestion> GetSuggestions()
    {
        lock (_lock)
        {
            return _activeSuggestions.ToList();
        }
    }

    public void DismissSuggestion(string host)
    {
        lock (_lock)
        {
            _dismissedHosts.Add(host);
            _suggestedHosts.Remove(host);
            _activeSuggestions.RemoveAll(s => s.Host == host);
        }
    }

    /// <summary>
    /// Gets all dismissed hosts.
    /// </summary>
    public IReadOnlySet<string> GetDismissedHosts()
    {
        lock (_lock)
        {
            return new HashSet<string>(_dismissedHosts);
        }
    }

    public Task<InterceptedRequest?> OnRequestAsync(InterceptedRequest request)
    {
        // Track the request by correlation ID so we can pair it with the response
        if (!string.IsNullOrEmpty(request.CorrelationId))
        {
            _pendingRequests[request.CorrelationId] = request;
        }
        return Task.FromResult<InterceptedRequest?>(request);
    }

    public Task<InterceptedResponse?> OnResponseAsync(InterceptedResponse response)
    {
        if (_disposed)
            return Task.FromResult<InterceptedResponse?>(response);

        // Try to pair with the original request
        InterceptedRequest? request = null;
        if (!string.IsNullOrEmpty(response.CorrelationId))
        {
            _pendingRequests.TryRemove(response.CorrelationId, out request);
        }

        if (request == null)
            return Task.FromResult<InterceptedResponse?>(response);

        // Run enabled detectors
        var context = new DetectorContext
        {
            Host = request.Host,
            Port = request.Port,
            Method = request.Method,
            Path = request.Path,
            StatusCode = response.StatusCode,
            RequestHeaders = request.Headers,
            ResponseHeaders = response.Headers,
            ResponseBody = response.Body
        };

        foreach (var detector in _detectors)
        {
            if (!_enabledDetectors.GetValueOrDefault(detector.Id, false))
                continue;

            var result = detector.Analyze(context);
            if (result != null)
            {
                EmitSuggestion(result);
            }
        }

        return Task.FromResult<InterceptedResponse?>(response);
    }

    private void EmitSuggestion(DetectorResult result)
    {
        lock (_lock)
        {
            // Don't re-suggest dismissed or already-suggested hosts
            if (_dismissedHosts.Contains(result.Host) || _suggestedHosts.Contains(result.Host))
                return;

            _suggestedHosts.Add(result.Host);
        }

        var suggestion = new PassthroughSuggestion
        {
            Timestamp = DateTime.UtcNow,
            Host = result.Host,
            DetectorId = result.DetectorId,
            DetectorName = result.DetectorName,
            Reason = result.Reason
        };

        lock (_lock)
        {
            _activeSuggestions.Add(suggestion);
        }

        _suggestions.Writer.TryWrite(suggestion);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _suggestions.Writer.Complete();
        _disposed = true;
    }
}
