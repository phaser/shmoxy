# PR: Make Proxy Multi-Threaded

## Problem

The current proxy implementation handles all connections on a single thread using async/await. While this works for I/O-bound operations, TLS encryption/decryption is CPU-bound and blocks the thread, causing noticeable performance degradation when multiple concurrent connections are active.

### Symptoms

- First request through proxy is fast
- Subsequent concurrent requests experience latency
- Page load times increase when multiple resources load in parallel (typical modern web pages)

### Root Cause

In `ProxyServer.StartAsync()` (line 108-118), connections are accepted in a loop and handlers are started with fire-and-forget:

```csharp
_ = HandleConnectionAsync(client);  // Not awaited, but runs on same thread
```

While handlers are not awaited, they all execute on the **same synchronization context**. When one connection performs CPU-intensive work (TLS handshake, encryption, decryption), it blocks other connections from progressing.

## Proposed Solution

### Option 1: Thread Pool Execution (Recommended)

Wrap each connection handler in `Task.Run()` to execute on the thread pool:

```csharp
_ = Task.Run(() => HandleConnectionAsync(client));
```

**Pros:**
- Simple change, minimal code modification
- Leverages .NET thread pool automatically
- Scales with available CPU cores

**Cons:**
- No limit on concurrent connections (could exhaust resources)
- Thread pool starvation possible under extreme load

### Option 2: Semaphore-Slimited Thread Pool

Add a configurable concurrency limit with semaphore:

```csharp
private readonly SemaphoreSlim _concurrencyLimiter = new(maxConcurrency);

// In StartAsync:
await _concurrencyLimiter.WaitAsync();
_ = Task.Run(async () =>
{
    try
    {
        await HandleConnectionAsync(client);
    }
    finally
    {
        _concurrencyLimiter.Release();
    }
});
```

**Pros:**
- Prevents resource exhaustion
- Configurable based on deployment environment

**Cons:**
- More complex implementation
- Need to tune the concurrency limit

## Configuration

Add new configuration option:

```csharp
public class ProxyConfig
{
    public int MaxConcurrentConnections { get; set; } = Environment.ProcessorCount * 2;
    // ... existing config
}
```

Default to `ProcessorCount * 2` as a reasonable starting point.

## Testing Strategy

### Test Categories

Tests are organized into categories:

- **Integration**: Functional e2e tests (run by default)
- **Performance**: Benchmark tests (run explicitly)

### Performance Benchmark

E2E performance test (`ProxyPerformanceTests.cs`) that:
1. Uses real-world sites with many parallel resources:
   - https://finance.yahoo.com
   - https://www.reddit.com
   - https://arstechnica.com
2. **Baseline**: Load all sites sequentially WITHOUT proxy, record total time
3. **Test**: Load all sites sequentially THROUGH proxy, record total time
4. Compare overhead percentage

### Running Tests

```bash
# Run all tests
dotnet test

# Run only integration tests
dotnet test --filter "Category=Integration"

# Run only performance tests
dotnet test --filter "Category=Performance"

# Run specific performance test
dotnet test --filter "FullyQualifiedName~ProxyPerformanceTests.Baseline_NoProxy_TotalLoadTime"
```

### Success Criteria

- Proxy overhead should be minimal (<50% for single-threaded, <20% for multi-threaded)
- After multi-threading: significant reduction in overhead percentage
- No resource exhaustion or errors under load

## Implementation Plan

1. [x] Document current behavior and performance issue (this file)
2. [x] Create performance e2e test to measure the issue
3. [x] Run baseline test to confirm performance problem
4. [x] Implement multi-threaded connection handling
5. [x] Add configuration for concurrency limits
6. [x] Run performance test to verify improvement
7. [x] Run existing test suite to ensure no regressions
8. [x] Add persistent root CA certificate storage

## Implementation Status

**COMPLETED:**
- Multi-threaded connection handling with `Task.Run()`
- Thread-safe certificate cache with `ConcurrentDictionary.GetOrAdd()`
- Configuration option `MaxConcurrentConnections` (currently unused - .NET thread pool manages concurrency)
- Persistent root CA certificate storage in `~/Library/Application Support/shmoxy`
- Performance test suite with 10 real-world sites

**DESIGN DECISION: SemaphoreSlim Removed**

Initial implementation used `SemaphoreSlim` to limit concurrent connections. This was **removed** because:
- The semaphore wrapped the **entire connection lifetime** including idle keep-alive connections
- Modern browsers keep connections open with HTTP keep-alive between requests
- Previous page's connections held semaphore slots while loading the next page
- Result: microsoft.com timed out after 30+ seconds waiting for slots

**Resolution:** `Task.Run()` alone is sufficient. The .NET thread pool already manages concurrency effectively without explicit limiting.

**Performance Results (10 sites):**

| Metric | Single-threaded | Multi-threaded | Improvement |
|--------|-----------------|----------------|-------------|
| Total time | 21693ms | 18825ms | **-13.2%** |
| Overhead % | +108% | +100% | **-8 points** |
| Timeouts | 1 (microsoft.com) | 0 | **Fixed** |
| Worst site (arstechnica) | +175% | +100% | **-75 points** |

**Biggest improvements:**
- microsoft.com: TIMEOUT → 2888ms (+50%) — **was broken, now works**
- arstechnica.com: 3108ms → 2257ms — **27% faster**
- mariusroosendaal.com: 2235ms → 1668ms — **25% faster**

**Certificate Persistence:**

Root CA is now persisted to `~/Library/Application Support/shmoxy/shmoxy-root-ca.pfx`:
- First run: generates new root CA, saves PFX + PEM
- Subsequent runs: loads existing PFX — same certificate every time
- Install PEM once in OS/browser trust store, works across all future runs
- Unit tests still use ephemeral certs (parameterless `TlsHandler()` constructor)
- E2E tests use persistent cert (via `ProxyConfig.CertStoragePath`)

## Open Questions

1. What is the acceptable latency threshold for proxied requests?
2. Should we expose concurrency limits via CLI options?
3. Do we need metrics/telemetry for connection queue depth?

## Future Improvements

- **Connection idle timeout**: Force cleanup of keep-alive connections after N seconds of inactivity
- **Connection pooling**: Cache connections to frequently accessed domains (googleapis.com, gstatic.com, etc.)
- **Certificate generation parallelism**: Currently serialized per-host via `ConcurrentDictionary.GetOrAdd()`
- **Performance target**: Current overhead is 100%, target is <50%

## Test Results Reference

Performance test reports are saved in `docs/performance/`:
- `20260319_145131_ProxyPerformanceTests_MultiThreaded.md` — Final multi-threaded results
- `20260319_140645_ProxyPerformanceTests_10Sites.md` — Single-threaded baseline (10 sites)
- `20260319_140400_ProxyPerformanceTests_Expanded.md` — Single-threaded baseline (6 sites)
- `20260319_135618_ProxyPerformanceTests.md` — Initial single-threaded baseline (3 sites)
