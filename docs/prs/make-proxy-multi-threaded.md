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
6. [ ] Run performance test to verify improvement (in progress - see known issues)
7. [ ] Run existing test suite to ensure no regressions

## Implementation Status

**COMPLETED:**
- Multi-threaded connection handling with `Task.Run()`
- Concurrency limiting with `SemaphoreSlim` (default: `ProcessorCount * 4`)
- Thread-safe certificate cache with `ConcurrentDictionary`
- Configuration option `MaxConcurrentConnections`

**KNOWN ISSUES:**
1. **Connection cleanup**: Some sites with persistent keep-alive connections (microsoft.com) experience timeouts after 30+ seconds. The semaphore slots are not being released promptly when connections stay open.
2. **Connection timeout handling**: Need to implement read/write timeouts to force cleanup of idle keep-alive connections.

**WORKAROUND:**
- For now, the high concurrency limit (56 on 14-core machines) allows most sites to complete successfully
- arstechnica.com improved from 41s to 2.5s (16x faster)

**TODO:**
- Add read/write timeouts to `CopyStreamAsync`
- Implement connection idle timeout for keep-alive connections
- Consider adding connection pool for frequently accessed domains

## Open Questions

1. What is the acceptable latency threshold for proxied requests?
2. Should we expose concurrency limits via CLI options?
3. Do we need metrics/telemetry for connection queue depth?
