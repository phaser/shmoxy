# Performance Test Results - ProxyPerformanceTests

**Date:** 2026-03-19 13:56:18
**Branch:** feature_make-proxy-muti-threaded
**Test Class:** `shmoxy.e2e.ProxyPerformanceTests`

## Test Summary

| Test | Status | Duration |
|------|--------|----------|
| Baseline_NoProxy_TotalLoadTime | ✅ Passed | 3s |
| Proxy_WithProxy_TotalLoadTime | ✅ Passed | 6s |

## Test Configuration

- **Test Sites:**
  - https://finance.yahoo.com
  - https://www.reddit.com
  - https://arstechnica.com

- **Timeout:** 60000ms per page load
- **Wait Condition:** `WaitUntilState.Load`
- **Browser:** Chromium (headless) with `--ignore-certificate-errors`

## Results

### Baseline_NoProxy_TotalLoadTime (PASSED)

**Total Load Time:** 2725ms for 3 sites

**Individual Site Load Times:**
- finance.yahoo.com: 1053ms
- www.reddit.com: 333ms
- arstechnica.com: 1338ms

**Artifacts Location:**
```
src/tests/shmoxy.e2e/bin/Debug/net10.0/playwright_run_a2b8a7b9/
```

### Proxy_WithProxy_TotalLoadTime (PASSED)

**Total Load Time:** 6054ms for 3 sites

**Individual Site Load Times:**
- finance.yahoo.com: 1827ms
- www.reddit.com: 693ms
- arstechnica.com: 3534ms

**Artifacts Location:**
```
src/tests/shmoxy.e2e/bin/Debug/net10.0/playwright_run_5e496e80/
```

## Performance Analysis

### Overhead Calculation

| Metric | Value |
|--------|-------|
| **Baseline (no proxy)** | 2725ms |
| **With Proxy** | 6054ms |
| **Absolute Overhead** | +3329ms |
| **Relative Overhead** | +122.2% |

### Per-Site Overhead

| Site | Baseline | With Proxy | Overhead |
|------|----------|------------|----------|
| finance.yahoo.com | 1053ms | 1827ms | +774ms (+73.5%) |
| www.reddit.com | 333ms | 693ms | +360ms (+108.1%) |
| arstechnica.com | 1338ms | 3534ms | +2196ms (+164.1%) |

## Observations

1. **Significant Proxy Overhead:** The proxy introduces approximately 122% overhead on total page load time.

2. **Resource-Heavy Sites Affected More:** arstechnica.com (which loads many parallel resources from cdn.arstechnica.net) shows the highest overhead at 164%, suggesting the single-threaded proxy bottleneck impacts sites with many concurrent resources.

3. **TLS Tunnel Establishment:** Logs show the proxy successfully established TLS tunnels to multiple domains including:
   - finance.yahoo.com, guce.yahoo.com, consent.yahoo.com
   - s.yimg.com (multiple parallel connections)
   - cdn.arstechnica.net (6+ parallel connections)
   - Various ad/analytics services (googletagservices.com, doubleclick.net, amazon-adsystem.com)

4. **Single-Threaded Bottleneck:** The test confirms the performance issue - sites with many parallel resources experience higher overhead due to the proxy's single-threaded connection handling.

## Issues Fixed

- **Certificate Validation:** Added `--ignore-certificate-errors` to browser launch args in `ProxyTestFixture.cs` to trust the proxy's dynamically generated certificates (matching the approach in `HttpsInterceptionTests`).

## Recommendations

1. **Implement Multi-Threading:** The primary bottleneck is the single-threaded connection handling. Implementing multi-threaded proxy connections would reduce overhead significantly.

2. **Connection Pooling:** Consider implementing connection pooling for frequently accessed domains.

3. **Parallel Resource Handling:** Optimize handling of sites that load many resources in parallel (like arstechnica.com with 6+ CDN connections).

## Artifacts

Trace files and screenshots are available in the respective `playwright_run_*` directories for detailed analysis.

---

*Generated automatically from test run on 2026-03-19*
