using Microsoft.Extensions.Hosting;

namespace shmoxy.frontend.services;

public class ProxyStatusService : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly CancellationTokenSource _shutdownCts = new();
    private FrontendProxyStatus _currentStatus = FrontendProxyStatus.Stopped;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private bool _disposed;

    private const int PollIntervalMs = 3000;

    public event Action? OnStatusChanged;

    public FrontendProxyStatus CurrentStatus => _currentStatus;

    public ProxyStatusService(ApiClient apiClient, IHostApplicationLifetime? lifetime = null)
    {
        _apiClient = apiClient;
        lifetime?.ApplicationStopping.Register(() => _shutdownCts.Cancel());
    }

    public void StartPolling()
    {
        if (_pollTask is not null && !_pollTask.IsCompleted)
            return;

        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        _pollTask = PollAsync(_cts.Token);
    }

    public void UpdateStatus(FrontendProxyStatus status)
    {
        _currentStatus = status;
        OnStatusChanged?.Invoke();
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var status = await _apiClient.GetProxyStatusAsync();
                if (status.IsRunning != _currentStatus.IsRunning || status.Address != _currentStatus.Address)
                {
                    _currentStatus = status;
                    OnStatusChanged?.Invoke();
                }
            }
            catch
            {
                if (_currentStatus.IsRunning)
                {
                    _currentStatus = FrontendProxyStatus.Stopped;
                    OnStatusChanged?.Invoke();
                }
            }

            try
            {
                await Task.Delay(PollIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _shutdownCts.Cancel();
        _cts?.Cancel();
        _shutdownCts.Dispose();
        _cts?.Dispose();
        _disposed = true;
    }
}
