# Performance Test Results - ProxyPerformanceTests (Expanded Site List)

**Date:** 2026-03-19 14:04:00  
**Branch:** feature/make-proxy-multi-threaded  
**Test Class:** `shmoxy.e2e.ProxyPerformanceTests`

## Test Summary

| Test | Status | Duration |
|------|--------|----------|
| Baseline_NoProxy_TotalLoadTime | ✅ Passed | 7s |
| Proxy_WithProxy_TotalLoadTime | ✅ Passed | 16s |

## Test Configuration

- **Test Sites (6 total):**
  - https://finance.yahoo.com
  - https://www.reddit.com
  - https://arstechnica.com
  - https://www.microsoft.com
  - https://european-union.europa.eu/index_en
  - https://developers.google.com/

- **Timeout:** 60000ms per page load
- **Wait Condition:** `WaitUntilState.Load`
- **Browser:** Chromium (headless) with `--ignore-certificate-errors`

## Results

### Baseline_NoProxy_TotalLoadTime (PASSED)

**Total Load Time:** 6610ms for 6 sites

**Individual Site Load Times:**
| Site | Load Time |
|------|-----------|
| finance.yahoo.com | 1039ms |
| www.reddit.com | 318ms |
| arstechnica.com | 1171ms |
| www.microsoft.com | 1905ms |
| european-union.europa.eu/index_en | 513ms |
| developers.google.com/ | 1662ms |

**Artifacts Location:** 
```
src/tests/shmoxy.e2e/bin/Debug/net10.0/playwright_run_a1604c7c/
```

### Proxy_WithProxy_TotalLoadTime (PASSED)

**Total Load Time:** 15232ms for 6 sites

**Individual Site Load Times:**
| Site | Load Time |
|------|-----------|
| finance.yahoo.com | 1966ms |
| www.reddit.com | 670ms |
| arstechnica.com | 3271ms |
| www.microsoft.com | 3529ms |
| european-union.europa.eu/index_en | 841ms |
| developers.google.com/ | 4953ms |

**Artifacts Location:**
```
src/tests/shmoxy.e2e/bin/Debug/net10.0/playwright_run_0c5018b6/
```

## Performance Analysis

### Overall Overhead

| Metric | Value |
|--------|-------|
| **Baseline (no proxy)** | 6610ms |
| **With Proxy** | 15232ms |
| **Absolute Overhead** | +8622ms |
| **Relative Overhead** | +130.4% |

### Per-Site Overhead Breakdown

| Site | Baseline | With Proxy | Overhead | Overhead % |
|------|----------|------------|----------|------------|
| finance.yahoo.com | 1039ms | 1966ms | +927ms | +89.2% |
| www.reddit.com | 318ms | 670ms | +352ms | +110.7% |
| arstechnica.com | 1171ms | 3271ms | +2100ms | +179.3% |
| www.microsoft.com | 1905ms | 3529ms | +1624ms | +85.2% |
| european-union.europa.eu | 513ms | 841ms | +328ms | +63.9% |
| developers.google.com | 1662ms | 4953ms | +3291ms | +198.0% |

### Site Categories by Overhead

**High Overhead (>150%):**
- developers.google.com: +198% (many Google services, fonts, analytics)
- arstechnica.com: +179% (many parallel CDN resources, ads)

**Medium Overhead (80-120%):**
- finance.yahoo.com: +89% (multiple subdomains, ads)
- www.microsoft.com: +85% (multiple Microsoft services, CDN)
- www.reddit.com: +111% (CDN resources, tracking)

**Low Overhead (<70%):**
- european-union.europa.eu: +64% (simpler site structure)

## Observations

1. **Consistent Overhead Pattern:** The proxy introduces approximately 130% overhead across 6 diverse sites, consistent with the previous 3-site test (122%).

2. **Resource-Heavy Sites Most Affected:** 
   - developers.google.com loads many Google services (fonts.googleapis.com, fonts.gstatic.com, www.gstatic.com, apis.google.com, google-analytics.com, googletagmanager.com)
   - arstechnica.com loads 6+ parallel connections to cdn.arstechnica.net plus ad networks

3. **TLS Handshake Bottleneck:** Each unique domain requires a separate TLS handshake through the proxy. Sites with many third-party resources (fonts, analytics, CDNs) suffer disproportionately.

4. **Single-Threaded Impact Confirmed:** The logs show many parallel CONNECT requests queuing up:
   - arstechnica.com: 6+ parallel cdn.arstechnica.net connections
   - microsoft.com: 10+ parallel connections to various microsoft.com subdomains
   - developers.google.com: 15+ parallel connections to googleapis.com, gstatic.com domains

5. **EU Site Performs Best:** european-union.europa.eu has the lowest overhead (64%), likely due to:
   - Fewer third-party resources
   - Simpler page structure
   - EU-based hosting (potentially better network path)

## Comparison with Previous Test (3 sites)

| Metric | 3-Site Test | 6-Site Test | Change |
|--------|-------------|-------------|--------|
| Baseline | 2725ms | 6610ms | +142% |
| With Proxy | 6054ms | 15232ms | +152% |
| Overhead % | 122% | 130% | +8 points |

The overhead percentage remains consistent, validating the test methodology.

## Recommendations

1. **Implement Multi-Threading:** Primary bottleneck is single-threaded connection handling. Each TLS handshake and data transfer blocks other connections.

2. **Connection Pooling:** Cache TLS connections to frequently accessed domains (googleapis.com, gstatic.com, microsoft.com subdomains).

3. **Prioritize Handshakes:** Implement priority queue for TLS handshakes to prevent head-of-line blocking.

4. **Target Performance:** After multi-threading implementation:
   - Goal: Reduce overhead from 130% to <50%
   - Stretch goal: <20% overhead

## Next Steps

1. Implement `Task.Run()` wrapper for connection handlers
2. Add configurable concurrency limits (`MaxConcurrentConnections`)
3. Re-run performance tests to measure improvement
4. Consider connection pooling for repeat domains

## Artifacts

Trace files and screenshots available in:
- `playwright_run_a1604c7c/` (baseline)
- `playwright_run_0c5018b6/` (with proxy)

---

*Generated automatically from test run on 2026-03-19*
