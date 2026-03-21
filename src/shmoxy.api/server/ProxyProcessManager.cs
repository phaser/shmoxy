using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.models.configuration;

namespace shmoxy.api.server;

public class ProxyProcessManager : IProxyProcessManager, IDisposable
{
    private readonly ILogger<ProxyProcessManager> _logger;
    private readonly IProxyIpcClient _ipcClient;
    private readonly IOptions<ApiConfig> _config;
    private readonly string _socketPath;

    private Process? _process;
    private ProxyInstanceState _currentState;
    private bool _disposed;
    private readonly CancellationTokenSource _healthCheckCts = new();
    private Task? _healthCheckTask;

    private const int HealthCheckIntervalMs = 100;
    private const int HealthCheckTimeoutMs = 5000;
    private const int ShutdownTimeoutMs = 10000;

    public event EventHandler<ProxyInstanceState>? OnStateChanged;

    public ProxyProcessManager(
        ILogger<ProxyProcessManager> logger,
        IProxyIpcClient ipcClient,
        IOptions<ApiConfig> config)
    {
        _logger = logger;
        _ipcClient = ipcClient;
        _config = config;
        _socketPath = config.Value.ProxyIpcSocketPath ?? Path.Combine(Path.GetTempPath(), $"shmoxy-{Guid.NewGuid()}.sock");
        _currentState = new ProxyInstanceState
        {
            State = ProxyProcessState.Stopped,
            SocketPath = _socketPath
        };
    }

    public async Task<ProxyInstanceState> StartAsync(CancellationToken ct = default)
    {
        if (_currentState.State == ProxyProcessState.Running || _currentState.State == ProxyProcessState.Starting)
        {
            _logger.LogWarning("Proxy is already {State}", _currentState.State);
            return _currentState;
        }

        var binaryPath = _config.Value.ProxyBinaryPath ?? "shmoxy";

        if (!await ValidateBinaryPathAsync(binaryPath, ct))
        {
            throw new InvalidOperationException($"Proxy binary not found: {binaryPath}");
        }

        UpdateState(new ProxyInstanceState
        {
            Id = _currentState.Id,
            State = ProxyProcessState.Starting,
            SocketPath = _socketPath,
            Port = _config.Value.ProxyPort,
            StartedAt = DateTime.UtcNow
        });

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = $"-p {_config.Value.ProxyPort} --ipc-socket {_socketPath}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += OnProcessOutput;
            _process.ErrorDataReceived += OnProcessError;
            _process.Exited += OnProcessExited;

            if (!_process.Start())
            {
                throw new InvalidOperationException("Failed to start proxy process");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            UpdateState(new ProxyInstanceState
            {
                Id = _currentState.Id,
                State = ProxyProcessState.Starting,
                ProcessId = _process.Id,
                SocketPath = _socketPath,
                Port = _config.Value.ProxyPort,
                StartedAt = _currentState.StartedAt
            });

            _logger.LogInformation("Started proxy process with PID {Pid} on socket {Socket}", _process.Id, _socketPath);

            if (await WaitForHealthyAsync(ct))
            {
                UpdateState(new ProxyInstanceState
                {
                    Id = _currentState.Id,
                    State = ProxyProcessState.Running,
                    ProcessId = _process.Id,
                    SocketPath = _socketPath,
                    Port = _config.Value.ProxyPort,
                    StartedAt = _currentState.StartedAt
                });

                StartHealthCheckLoop();
                _logger.LogInformation("Proxy process is healthy and running");
            }
            else
            {
                throw new TimeoutException("Proxy process failed to become healthy");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start proxy process");
            UpdateState(new ProxyInstanceState
            {
                Id = _currentState.Id,
                State = ProxyProcessState.Crashed,
                SocketPath = _socketPath,
                Port = _config.Value.ProxyPort,
                StartedAt = _currentState.StartedAt,
                StoppedAt = DateTime.UtcNow,
                ExitReason = ex.Message
            });
            throw;
        }

        return _currentState;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_currentState.State == ProxyProcessState.Stopped || _currentState.State == ProxyProcessState.Stopping)
        {
            _logger.LogWarning("Proxy is already {State}", _currentState.State);
            return;
        }

        UpdateState(new ProxyInstanceState
        {
            Id = _currentState.Id,
            State = ProxyProcessState.Stopping,
            ProcessId = _currentState.ProcessId,
            SocketPath = _currentState.SocketPath,
            Port = _currentState.Port,
            StartedAt = _currentState.StartedAt,
            StoppedAt = DateTime.UtcNow
        });

        _healthCheckCts.Cancel();

        if (_process != null && !_process.HasExited)
        {
            try
            {
                _logger.LogInformation("Attempting graceful shutdown via IPC...");
                using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                shutdownCts.CancelAfter(ShutdownTimeoutMs);

                await _ipcClient.ShutdownAsync(shutdownCts.Token);

                if (_process.WaitForExit(ShutdownTimeoutMs))
                {
                    _logger.LogInformation("Proxy process shut down gracefully");
                }
                else
                {
                    _logger.LogWarning("Graceful shutdown timed out, force killing...");
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Graceful shutdown failed, force killing...");
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(ct);
            }
        }

        CleanupSocketFile();

        UpdateState(new ProxyInstanceState
        {
            Id = _currentState.Id,
            State = ProxyProcessState.Stopped,
            ProcessId = null,
            SocketPath = _currentState.SocketPath,
            Port = null,
            StartedAt = _currentState.StartedAt,
            StoppedAt = DateTime.UtcNow,
            ExitReason = "Stopped by user"
        });

        _logger.LogInformation("Proxy process stopped");
    }

    public Task<ProxyInstanceState?> GetStateAsync()
    {
        return Task.FromResult<ProxyInstanceState?>(_currentState);
    }

    public Task<bool> IsRunningAsync()
    {
        return Task.FromResult(_currentState.State == ProxyProcessState.Running);
    }

    public async Task<string> GetRootCertPemAsync(CancellationToken ct = default)
    {
        if (_currentState.State != ProxyProcessState.Running)
        {
            throw new InvalidOperationException("Proxy must be running to get certificate");
        }

        var handler = new HttpClientHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var tempLogger = loggerFactory.CreateLogger<ProxyIpcClient>();
        var client = new ProxyIpcClient(httpClient, tempLogger);
        return await client.GetRootCertPemAsync(ct);
    }

    public async Task<byte[]> GetRootCertDerAsync(CancellationToken ct = default)
    {
        if (_currentState.State != ProxyProcessState.Running)
        {
            throw new InvalidOperationException("Proxy must be running to get certificate");
        }

        var handler = new HttpClientHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var tempLogger = loggerFactory.CreateLogger<ProxyIpcClient>();
        var client = new ProxyIpcClient(httpClient, tempLogger);
        return await client.GetRootCertDerAsync(ct);
    }

    private void UpdateState(ProxyInstanceState newState)
    {
        var oldState = _currentState;
        _currentState = newState;
        OnStateChanged?.Invoke(this, newState);
        _logger.LogDebug("Proxy state changed from {OldState} to {NewState}", oldState.State, newState.State);
    }

    private async Task<bool> ValidateBinaryPathAsync(string binaryPath, CancellationToken ct)
    {
        if (Path.IsPathRooted(binaryPath))
        {
            return File.Exists(binaryPath);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = new Process { StartInfo = startInfo };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(2000);

            process.Start();
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> WaitForHealthyAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(HealthCheckTimeoutMs);

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < HealthCheckTimeoutMs && !cts.Token.IsCancellationRequested)
        {
            try
            {
                if (await _ipcClient.IsHealthyAsync(cts.Token))
                {
                    return true;
                }
            }
            catch
            {
            }

            await Task.Delay(HealthCheckIntervalMs, cts.Token);
        }

        return false;
    }

    private void StartHealthCheckLoop()
    {
        _healthCheckTask = Task.Run(async () =>
        {
            while (!_healthCheckCts.Token.IsCancellationRequested && _currentState.State == ProxyProcessState.Running)
            {
                try
                {
                    if (!await _ipcClient.IsHealthyAsync(_healthCheckCts.Token))
                    {
                        _logger.LogWarning("Proxy health check failed");
                        UpdateState(new ProxyInstanceState
                        {
                            Id = _currentState.Id,
                            State = ProxyProcessState.Crashed,
                            ProcessId = _currentState.ProcessId,
                            SocketPath = _currentState.SocketPath,
                            Port = _currentState.Port,
                            StartedAt = _currentState.StartedAt,
                            StoppedAt = DateTime.UtcNow,
                            ExitReason = "Health check failed"
                        });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Health check error");
                }

                await Task.Delay(HealthCheckIntervalMs, _healthCheckCts.Token);
            }
        });
    }

    private void OnProcessOutput(object? sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            _logger.LogInformation("Proxy stdout: {Data}", e.Data);
        }
    }

    private void OnProcessError(object? sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            _logger.LogError("Proxy stderr: {Data}", e.Data);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _logger.LogInformation("Proxy process exited with code {ExitCode}", _process?.ExitCode ?? -1);
        CleanupSocketFile();

        UpdateState(new ProxyInstanceState
        {
            Id = _currentState.Id,
            State = ProxyProcessState.Stopped,
            ProcessId = null,
            SocketPath = _currentState.SocketPath,
            Port = null,
            StartedAt = _currentState.StartedAt,
            StoppedAt = DateTime.UtcNow,
            ExitReason = _process != null ? $"Exit code: {_process.ExitCode}" : "Process exited"
        });
    }

    private void CleanupSocketFile()
    {
        if (!string.IsNullOrEmpty(_currentState.SocketPath) && File.Exists(_currentState.SocketPath))
        {
            try
            {
                File.Delete(_currentState.SocketPath);
                _logger.LogDebug("Cleaned up socket file {Socket}", _currentState.SocketPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up socket file {Socket}", _currentState.SocketPath);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _healthCheckCts.Cancel();
        _healthCheckTask?.Wait(1000);

        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(1000);
            }
            catch
            {
            }
            _process.Dispose();
        }

        CleanupSocketFile();
        _disposed = true;
    }
}
