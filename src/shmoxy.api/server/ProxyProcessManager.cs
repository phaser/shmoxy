using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.models.configuration;

namespace shmoxy.api.server;

public class ProxyProcessManager : IProxyProcessManager, IDisposable
{
    private readonly ILogger<ProxyProcessManager> _logger;
    private readonly IOptions<ApiConfig> _config;
    private readonly IConfigPersistence _configPersistence;
    private readonly string _socketPath;
    private readonly IProxyIpcClient? _injectedIpcClient;

    private Process? _process;
    private ProxyInstanceState _currentState;
    private bool _disposed;
    private readonly CancellationTokenSource _healthCheckCts = new();
    private Task? _healthCheckTask;
    private ProxyIpcClient? _socketIpcClient;
    private HttpClient? _socketHttpClient;
    private int? _portOverride;

    private const int HealthCheckIntervalMs = 100;
    private const int HealthCheckTimeoutMs = 15000;
    private const int ShutdownTimeoutMs = 10000;

    public event EventHandler<ProxyInstanceState>? OnStateChanged;

    public ProxyProcessManager(
        ILogger<ProxyProcessManager> logger,
        IProxyIpcClient ipcClient,
        IOptions<ApiConfig> config,
        IConfigPersistence configPersistence)
    {
        _logger = logger;
        _config = config;
        _configPersistence = configPersistence;
        _socketPath = config.Value.ProxyIpcSocketPath ?? Path.Combine(Path.GetTempPath(), $"shmoxy-{Guid.NewGuid()}.sock");
        // Use the DI-injected client when a socket path is pre-configured (it's already wired to that socket).
        // When the socket path is generated dynamically, we must create our own client at runtime.
        _injectedIpcClient = config.Value.ProxyIpcSocketPath is not null ? ipcClient : null;
        _currentState = new ProxyInstanceState
        {
            State = ProxyProcessState.Stopped,
            SocketPath = _socketPath
        };
    }

    public IProxyIpcClient GetIpcClient() => GetOrCreateSocketIpcClient();

    private IProxyIpcClient GetOrCreateSocketIpcClient()
    {
        if (_injectedIpcClient is not null)
            return _injectedIpcClient;

        if (_socketIpcClient is not null)
            return _socketIpcClient;

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                var endPoint = new UnixDomainSocketEndPoint(_socketPath);
                await socket.ConnectAsync(endPoint, token);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };

        _socketHttpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _socketIpcClient = new ProxyIpcClient(_socketHttpClient, loggerFactory.CreateLogger<ProxyIpcClient>());
        return _socketIpcClient;
    }

    public async Task<ProxyInstanceState> StartAsync(CancellationToken ct = default)
    {
        if (_currentState.State is ProxyProcessState.Running or ProxyProcessState.Starting)
        {
            _logger.LogWarning("Proxy is already {State}", _currentState.State);
            return _currentState;
        }

        var (fileName, baseArguments) = await ResolveBinaryAsync(_config.Value.ProxyBinaryPath, ct);

        // Load persisted config for port (and to re-apply after startup)
        var persistedConfig = await _configPersistence.LoadAsync(ct);
        var port = _portOverride ?? persistedConfig?.Port ?? _config.Value.ProxyPort;

        UpdateState(new ProxyInstanceState
        {
            Id = _currentState.Id,
            State = ProxyProcessState.Starting,
            SocketPath = _socketPath,
            Port = port,
            StartedAt = DateTime.UtcNow
        });

        try
        {
            var proxyArgs = $"-p {port} --ipc-socket {_socketPath}";
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = string.IsNullOrEmpty(baseArguments)
                    ? proxyArgs
                    : $"{baseArguments} {proxyArgs}",
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
                Port = port,
                StartedAt = _currentState.StartedAt
            });

            _logger.LogInformation("Started proxy process with PID {Pid} on socket {Socket}", _process.Id, _socketPath);

            if (await WaitForHealthyAsync(ct))
            {
                string? proxyVersion = null;
                try
                {
                    var status = await GetOrCreateSocketIpcClient().GetStatusAsync(ct);
                    proxyVersion = status.Version;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to fetch proxy version during startup");
                }

                UpdateState(new ProxyInstanceState
                {
                    Id = _currentState.Id,
                    State = ProxyProcessState.Running,
                    ProcessId = _process.Id,
                    SocketPath = _socketPath,
                    Port = port,
                    StartedAt = _currentState.StartedAt,
                    ProxyVersion = proxyVersion
                });

                // Apply persisted config to the new proxy process
                if (persistedConfig != null)
                {
                    try
                    {
                        await GetOrCreateSocketIpcClient().UpdateConfigAsync(persistedConfig, ct);
                        _logger.LogInformation("Applied persisted configuration to proxy");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to apply persisted configuration to proxy");
                    }
                }

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
                Port = port,
                StartedAt = _currentState.StartedAt,
                StoppedAt = DateTime.UtcNow,
                ExitReason = ex.Message
            });
            throw;
        }

        return _currentState;
    }

    public async Task StopAsync(ShutdownSource source = ShutdownSource.User, CancellationToken ct = default)
    {
        var shutdownRequestedAt = DateTime.UtcNow;

        if (_currentState.State == ProxyProcessState.Stopped || _currentState.State == ProxyProcessState.Stopping)
        {
            _logger.LogWarning("Proxy is already {State}", _currentState.State);
            return;
        }

        _logger.LogInformation(
            "Proxy shutdown requested at {ShutdownRequestedAt} by {ShutdownSource}",
            shutdownRequestedAt, source);

        UpdateState(new ProxyInstanceState
        {
            Id = _currentState.Id,
            State = ProxyProcessState.Stopping,
            ProcessId = _currentState.ProcessId,
            SocketPath = _currentState.SocketPath,
            Port = _currentState.Port,
            StartedAt = _currentState.StartedAt,
            StoppedAt = shutdownRequestedAt
        });

        _healthCheckCts.Cancel();

        if (_process != null)
        {
            if (_process.HasExited)
            {
                _logger.LogInformation("Proxy process already exited with code {ExitCode}", _process.ExitCode);
            }
            else
            {
                try
                {
                    _logger.LogInformation("Attempting graceful shutdown via IPC...");
                    using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    shutdownCts.CancelAfter(ShutdownTimeoutMs);

                    await GetOrCreateSocketIpcClient().ShutdownAsync(shutdownCts.Token);

                    if (_process.WaitForExit(ShutdownTimeoutMs))
                    {
                        _logger.LogInformation("Proxy process shut down gracefully");
                    }
                    else
                    {
                        _logger.LogWarning("Graceful shutdown timed out after {TimeoutMs}ms, force killing...", ShutdownTimeoutMs);
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit();
                    }
                }
                catch (Exception) when (_process.HasExited)
                {
                    _logger.LogInformation("Proxy process exited during shutdown (code {ExitCode})", _process.ExitCode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Graceful shutdown failed, force killing...");
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        await _process.WaitForExitAsync(ct);
                    }
                }
            }
        }

        CleanupSocketFile();

        var exitReason = source switch
        {
            ShutdownSource.User => "Stopped by user",
            ShutdownSource.System => "Stopped by system (application shutdown)",
            ShutdownSource.HealthCheck => "Stopped after health check failure",
            ShutdownSource.Dispose => "Stopped during resource disposal",
            _ => $"Stopped ({source})"
        };

        UpdateState(new ProxyInstanceState
        {
            Id = _currentState.Id,
            State = ProxyProcessState.Stopped,
            ProcessId = null,
            SocketPath = _currentState.SocketPath,
            Port = null,
            StartedAt = _currentState.StartedAt,
            StoppedAt = DateTime.UtcNow,
            ExitReason = exitReason
        });

        _logger.LogInformation("Proxy process stopped (source: {ShutdownSource}, reason: {ExitReason})", source, exitReason);
    }

    public async Task<ProxyInstanceState> RestartAsync(int? portOverride = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Restarting proxy{PortInfo}", portOverride.HasValue ? $" on port {portOverride.Value}" : "");

        if (portOverride.HasValue)
            _portOverride = portOverride.Value;

        await StopAsync(ShutdownSource.System, ct);
        return await StartAsync(ct);
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

        return await GetOrCreateSocketIpcClient().GetRootCertPemAsync(ct);
    }

    public async Task<byte[]> GetRootCertDerAsync(CancellationToken ct = default)
    {
        if (_currentState.State != ProxyProcessState.Running)
        {
            throw new InvalidOperationException("Proxy must be running to get certificate");
        }

        return await GetOrCreateSocketIpcClient().GetRootCertDerAsync(ct);
    }

    private void UpdateState(ProxyInstanceState newState)
    {
        var oldState = _currentState;
        _currentState = newState;
        OnStateChanged?.Invoke(this, newState);
        _logger.LogDebug("Proxy state changed from {OldState} to {NewState}", oldState.State, newState.State);
    }

    /// <summary>
    /// Resolves the proxy binary to a (FileName, BaseArguments) tuple.
    /// Supports: native executables on PATH, absolute paths, and .dll files run via dotnet.
    /// </summary>
    private async Task<(string FileName, string BaseArguments)> ResolveBinaryAsync(string? configuredPath, CancellationToken ct)
    {
        // 1. Explicit absolute path
        if (configuredPath is not null && Path.IsPathRooted(configuredPath))
        {
            if (configuredPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(configuredPath))
                    throw new InvalidOperationException($"Proxy DLL not found: {configuredPath}");
                return ("dotnet", configuredPath);
            }

            if (!File.Exists(configuredPath))
                throw new InvalidOperationException($"Proxy binary not found: {configuredPath}");
            return (configuredPath, string.Empty);
        }

        // 2. Try native executable on PATH (e.g., "shmoxy" after nix build)
        var executableName = configuredPath ?? "shmoxy";
        if (await TryRunAsync(executableName, "--version", ct))
        {
            return (executableName, string.Empty);
        }

        // 3. Look for shmoxy.dll next to the running API assembly or in a proxy/ subdirectory
        var appDir = AppContext.BaseDirectory;
        var dllPath = Path.Combine(appDir, "shmoxy.dll");
        if (File.Exists(dllPath))
        {
            _logger.LogInformation("Resolved proxy binary to DLL: {DllPath}", dllPath);
            return ("dotnet", dllPath);
        }

        // 4. Look in proxy/ subdirectory (Docker layout where proxy is published separately)
        var subDirDllPath = Path.Combine(appDir, "proxy", "shmoxy.dll");
        if (File.Exists(subDirDllPath))
        {
            _logger.LogInformation("Resolved proxy binary to DLL: {DllPath}", subDirDllPath);
            return ("dotnet", subDirDllPath);
        }

        throw new InvalidOperationException(
            $"Proxy binary not found. Searched for '{executableName}' on PATH, '{dllPath}', and '{subDirDllPath}'");
    }

    private static async Task<bool> TryRunAsync(string fileName, string arguments, CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
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

        // When using a dynamically-created socket, wait for the file to appear before
        // attempting IPC health checks to avoid noisy connection errors during startup.
        if (_injectedIpcClient is null)
        {
            while (!File.Exists(_socketPath) && !cts.Token.IsCancellationRequested)
            {
                _logger.LogDebug("Waiting for socket file {Socket} to appear ({Elapsed}ms)", _socketPath, stopwatch.ElapsedMilliseconds);
                try
                {
                    await Task.Delay(HealthCheckIntervalMs, cts.Token);
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                {
                    _logger.LogWarning("Timed out waiting for socket file after {Timeout}ms", HealthCheckTimeoutMs);
                    return false;
                }
            }
        }

        while (stopwatch.ElapsedMilliseconds < HealthCheckTimeoutMs && !cts.Token.IsCancellationRequested)
        {
            try
            {
                if (await GetOrCreateSocketIpcClient().IsHealthyAsync(cts.Token))
                {
                    return true;
                }

                _logger.LogDebug("Health check returned not-listening after {Elapsed}ms", stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Health check timed out after {Timeout}ms", HealthCheckTimeoutMs);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Health check attempt failed after {Elapsed}ms", stopwatch.ElapsedMilliseconds);
            }

            try
            {
                await Task.Delay(HealthCheckIntervalMs, cts.Token);
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Health check timed out after {Timeout}ms", HealthCheckTimeoutMs);
                return false;
            }
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
                    if (!await GetOrCreateSocketIpcClient().IsHealthyAsync(_healthCheckCts.Token))
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
                catch (OperationCanceledException) when (_healthCheckCts.Token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Health check error");
                }

                try
                {
                    await Task.Delay(HealthCheckIntervalMs, _healthCheckCts.Token);
                }
                catch (OperationCanceledException) when (_healthCheckCts.Token.IsCancellationRequested)
                {
                    return;
                }
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

        // Attempt graceful shutdown if proxy is still running
        if (_currentState.State is ProxyProcessState.Running or ProxyProcessState.Starting)
        {
            try
            {
                StopAsync(ShutdownSource.Dispose, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Graceful shutdown during dispose failed, force killing");
            }
        }

        _healthCheckCts.Cancel();
        _healthCheckTask?.Wait(1000);

        _socketIpcClient?.Dispose();
        _socketHttpClient?.Dispose();

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
