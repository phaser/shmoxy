# Performance Test Results - ProxyPerformanceTests (10 Sites)

**Date:** 2026-03-19 14:06:45  
**Branch:** feature/make-proxy-multi-threaded  
**Test Class:** `shmoxy.e2e.ProxyPerformanceTests`

## Test Summary

| Test | Status | Duration |
|------|--------|----------|
| Baseline_NoProxy_TotalLoadTime | ✅ Passed | 11s |
| Proxy_WithProxy_TotalLoadTime | ✅ Passed | 22s |

## Test Configuration

- **Test Sites (10 total):**
  - https://finance.yahoo.com
  - https://www.reddit.com
  - https://arstechnica.com
  - https://www.microsoft.com
  - https://european-union.europa.eu/index_en
  - https://developers.google.com/
  - https://www.andimayr.de/
  - https://playhundreds.com/
  - https://mariusroosendaal.com/
  - https://www.radoslavholan.cz/

- **Timeout:** 60000ms per page load
- **Wait Condition:** `WaitUntilState.Load`
- **Browser:** Chromium (headless) with `--ignore-certificate-errors`

## Results

### Baseline_NoProxy_TotalLoadTime (PASSED)

**Total Load Time:** 10424ms for 10 sites

| Site | Load Time |
|------|-----------|
| finance.yahoo.com | 996ms |
| www.reddit.com | 295ms |
| arstechnica.com | 1128ms |
| www.microsoft.com | 1928ms |
| european-union.europa.eu/index_en | 434ms |
| developers.google.com/ | 2645ms |
| www.andimayr.de/ | 336ms |
| playhundreds.com | 1009ms |
| mariusroosendaal.com | 893ms |
| www.radoslavholan.cz/ | 754ms |

**Artifacts Location:** 
```
src/tests/shmoxy.e2e/bin/Debug/net10.0/playwright_run_3189678d/
```

### Proxy_WithProxy_TotalLoadTime (PASSED)

**Total Load Time:** 21693ms for 10 sites

| Site | Load Time |
|------|-----------|
| finance.yahoo.com | 1954ms |
| www.reddit.com | 653ms |
| arstechnica.com | 3108ms |
| www.microsoft.com | 3478ms |
| european-union.europa.eu/index_en | 916ms |
| developers.google.com/ | 4464ms |
| www.andimayr.de/ | 1364ms |
| playhundreds.com | 2113ms |
| mariusroosendaal.com | 2235ms |
| www.radoslavholan.cz/ | 1404ms |

**Artifacts Location:**
```
src/tests/shmoxy.e2e/bin/Debug/net10.0/playwright_run_9d6b2e4b/
```

## Performance Analysis

### Overall Overhead

| Metric | Value |
|--------|-------|
| **Baseline (no proxy)** | 10424ms |
| **With Proxy** | 21693ms |
| **Absolute Overhead** | +11269ms |
| **Relative Overhead** | +108.1% |

### Per-Site Overhead Breakdown

| Site | Baseline | With Proxy | Overhead | Overhead % |
|------|----------|------------|----------|------------|
| finance.yahoo.com | 996ms | 1954ms | +958ms | +96.2% |
| www.reddit.com | 295ms | 653ms | +358ms | +121.4% |
| arstechnica.com | 1128ms | 3108ms | +1980ms | +175.5% |
| www.microsoft.com | 1928ms | 3478ms | +1550ms | +80.4% |
| european-union.europa.eu | 434ms | 916ms | +482ms | +111.1% |
| developers.google.com | 2645ms | 4464ms | +1819ms | +68.8% |
| www.andimayr.de/ | 336ms | 1364ms | +1028ms | +306.0% |
| playhundreds.com | 1009ms | 2113ms | +1104ms | +109.4% |
| mariusroosendaal.com | 893ms | 2235ms | +1342ms | +150.3% |
| www.radoslavholan.cz/ | 754ms | 1404ms | +650ms | +86.2% |

### Site Categories by Overhead

**Very High Overhead (>200%):**
- www.andimayr.de: +306% (small site, many Format.com CDN resources)

**High Overhead (150-200%):**
- arstechnica.com: +176% (many parallel CDN resources, ads)
- mariusroosendaal.com: +150% (Cargo.site platform with multiple subdomains)

**Medium Overhead (100-150%):**
- www.reddit.com: +121%
- european-union.europa.eu: +111%
- playhundreds.com: +109%

**Low Overhead (<100%):**
- finance.yahoo.com: +96%
- www.radoslavholan.cz: +86%
- www.microsoft.com: +80%
- developers.google.com: +69%

## Key Observations

### 1. Small Sites Show Higher Percentage Overhead

Smaller sites (andimayr.de, mariusroosendaal.com) show the highest percentage overhead despite having fewer resources. This is because:
- Fixed TLS handshake cost is a larger percentage of total load time
- Single-threaded proxy handles each handshake sequentially
- Small sites load quickly without proxy, making proxy overhead more visible

### 2. Large Sites Benefit from Resource Parallelism

Large sites (developers.google.com, microsoft.com) show lower percentage overhead because:
- Base load time is dominated by resource download, not TLS handshakes
- TLS handshake cost is amortized across many resources
- Page load waits for many resources, masking handshake latency

### 3. Platform-Specific Patterns

**Format.com sites** (andimayr.de, playhundreds.com, mariusroosendaal.com):
- Multiple connections to format.creatorcdn.com, bucket0.format-assets.com
- Cargo.site platform uses build.cargo.site, api.cargo.site, type.cargo.site
- Cloudflare integration adds cloudflareinsights.com connections

**Google-heavy sites** (developers.google.com):
- Many connections to googleapis.com, gstatic.com, google-analytics.com
- Despite many domains, overhead is only 69% (amortized across large page)

### 4. Consistent Overhead Across Test Runs

| Test Run | Sites | Baseline | Proxy | Overhead % |
|----------|-------|----------|-------|------------|
| Run 1 | 3 sites | 2725ms | 6054ms | 122% |
| Run 2 | 6 sites | 6610ms | 15232ms | 130% |
| Run 3 | 10 sites | 10424ms | 21693ms | 108% |

Overhead consistently ranges 108-130%, validating test methodology.

## TLS Connection Analysis

### Most Common Third-Party Domains

From proxy logs, frequently observed domains:
- googleapis.com, gstatic.com (fonts, APIs)
- microsoft.com subdomains (cdn-dynmedia-1, c.s-microsoft.com, uhf.microsoft.com)
- arstechnica.net CDN (6+ parallel connections)
- format.creatorcdn.com, bucket0.format-assets.com
- cloudflareinsights.com
- google-analytics.com, googletagmanager.com
- doubleclick.net (ads)

### Connection Queuing Evidence

Logs show sequential TLS tunnel establishment even for parallel requests:
```
[2026-03-19T12:06:02.8324360Z] INFO: CONNECT request to cdn.arstechnica.net:443
[2026-03-19T12:06:02.8325770Z] INFO: CONNECT request to cdn.arstechnica.net:443
[2026-03-19T12:06:02.8339570Z] INFO: CONNECT request to cdn.arstechnica.net:443
...
[2026-03-19T12:06:03.0026560Z] INFO: TLS tunnel established to cdn.arstechnica.net:443
[2026-03-19T12:06:03.0537640Z] INFO: TLS tunnel established to cdn.arstechnica.net:443
```

Multiple CONNECT requests arrive within milliseconds, but TLS tunnels are established sequentially over ~200ms period.

## Recommendations

1. **Implement Multi-Threading:** Primary bottleneck confirmed - TLS handshakes queue up on single thread.

2. **Target Performance Goals:**
   - Current: 108% overhead
   - After multi-threading: <50% overhead
   - Optimal: <20% overhead

3. **Connection Pooling:** Cache TLS connections to frequently accessed domains (googleapis.com, gstatic.com, format.creatorcdn.com).

4. **Prioritize Handshakes:** Implement priority queue - critical domain handshakes should complete before third-party analytics/ads.

## Next Steps

1. Implement `Task.Run()` wrapper for connection handlers in `ProxyServer.StartAsync()`
2. Add `MaxConcurrentConnections` configuration option
3. Re-run 10-site test suite to measure improvement
4. Consider connection pooling for repeat domains within same page load

## Artifacts

Trace files and screenshots available in:
- `playwright_run_3189678d/` (baseline)
- `playwright_run_9d6b2e4b/` (with proxy)

---

*Generated automatically from test run on 2026-03-19*
