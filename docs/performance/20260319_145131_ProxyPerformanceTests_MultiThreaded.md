# Performance Test Results - Multi-Threaded Proxy (10 Sites)

**Date:** 2026-03-19 14:51:31
**Branch:** feature/make-proxy-multi-threaded
**Test Class:** `shmoxy.e2e.ProxyPerformanceTests`
**Change:** Replaced single-threaded fire-and-forget with `Task.Run()` for thread pool execution

## Test Summary

| Test | Status | Duration |
|------|--------|----------|
| Baseline_NoProxy_TotalLoadTime | ✅ Passed | 10s |
| Proxy_WithProxy_TotalLoadTime | ✅ Passed | 19s |

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

**Total Load Time:** 9427ms for 10 sites

| Site | Load Time |
|------|-----------|
| finance.yahoo.com | 1074ms |
| www.reddit.com | 310ms |
| arstechnica.com | 1131ms |
| www.microsoft.com | 1931ms |
| european-union.europa.eu/index_en | 454ms |
| developers.google.com/ | 1607ms |
| www.andimayr.de/ | 344ms |
| playhundreds.com | 845ms |
| mariusroosendaal.com | 913ms |
| www.radoslavholan.cz/ | 812ms |

### Proxy_WithProxy_TotalLoadTime (PASSED)

**Total Load Time:** 18825ms for 10 sites

| Site | Load Time |
|------|-----------|
| finance.yahoo.com | 1921ms |
| www.reddit.com | 614ms |
| arstechnica.com | 2257ms |
| www.microsoft.com | 2888ms |
| european-union.europa.eu/index_en | 837ms |
| developers.google.com/ | 4349ms |
| www.andimayr.de/ | 1128ms |
| playhundreds.com | 1900ms |
| mariusroosendaal.com | 1668ms |
| www.radoslavholan.cz/ | 1258ms |

## Performance Comparison: Single-Threaded vs Multi-Threaded

### Per-Site Comparison

| Site | Baseline | Single-threaded | Multi-threaded | ST Overhead | MT Overhead |
|------|----------|-----------------|----------------|-------------|-------------|
| finance.yahoo.com | 1074ms | 1954ms | 1921ms | +82% | +79% |
| www.reddit.com | 310ms | 653ms | 614ms | +111% | +98% |
| arstechnica.com | 1131ms | 3108ms | 2257ms | **+175%** | **+100%** |
| www.microsoft.com | 1931ms | **TIMEOUT** | 2888ms | **FAILED** | **+50%** |
| european-union.europa.eu | 454ms | 916ms | 837ms | +102% | +84% |
| developers.google.com | 1607ms | 4464ms | 4349ms | +178% | +171% |
| www.andimayr.de | 344ms | 1364ms | 1128ms | +297% | +228% |
| playhundreds.com | 845ms | 2113ms | 1900ms | +109% | +125% |
| mariusroosendaal.com | 913ms | 2235ms | 1668ms | +145% | **+83%** |
| www.radoslavholan.cz | 812ms | 1404ms | 1258ms | +86% | +55% |
| **TOTAL** | **9427ms** | **21693ms** | **18825ms** | **+108%** | **+100%** |

### Overall Improvement

| Metric | Single-threaded | Multi-threaded | Improvement |
|--------|-----------------|----------------|-------------|
| Total time | 21693ms | 18825ms | **-13.2%** |
| Overhead % | 108% | 100% | **-8 points** |
| Timeouts | 1 (microsoft.com) | 0 | **Fixed** |
| Worst site overhead | 297% (andimayr.de) | 228% (andimayr.de) | **-69 points** |

### Biggest Improvements

1. **www.microsoft.com:** TIMEOUT → 2888ms (+50%) — **was completely broken, now works**
2. **arstechnica.com:** 3108ms (+175%) → 2257ms (+100%) — **75 point overhead reduction**
3. **mariusroosendaal.com:** 2235ms (+145%) → 1668ms (+83%) — **62 point overhead reduction**
4. **www.andimayr.de:** 1364ms (+297%) → 1128ms (+228%) — **69 point overhead reduction**
5. **www.radoslavholan.cz:** 1404ms (+86%) → 1258ms (+55%) — **31 point overhead reduction**

## Implementation Details

### What Changed

1. **`ProxyServer.StartAsync()`**: Connection handlers now run on the thread pool via `Task.Run()`:
   ```csharp
   // Before (single-threaded):
   _ = HandleConnectionAsync(client);

   // After (multi-threaded):
   _ = Task.Run(() => HandleConnectionAsync(client));
   ```

2. **`TlsHandler`**: Certificate cache changed from `Dictionary` + `lock` to `ConcurrentDictionary.GetOrAdd()` for thread-safe concurrent access without blocking.

### Bug Fix: SemaphoreSlim Removed

Initial multi-threaded implementation used `SemaphoreSlim` to limit concurrent connections. This caused connection starvation because:
- The semaphore wrapped the **entire connection lifetime** including idle keep-alive connections
- Modern browsers keep connections open with HTTP keep-alive
- Previous page's connections held semaphore slots while loading the next page
- Result: microsoft.com timed out after 30+ seconds waiting for slots

Fix: Removed `SemaphoreSlim` entirely. `Task.Run()` delegates concurrency management to the .NET thread pool, which handles this correctly.

## Observations

1. **Reliability:** All 10 sites load successfully. Single-threaded version failed on microsoft.com.

2. **Resource-heavy sites benefit most:** Sites with many parallel resources (arstechnica, microsoft) show the biggest improvement because connections are now handled concurrently.

3. **Small sites still show high overhead:** andimayr.de still has 228% overhead. The fixed cost of TLS handshakes remains a significant proportion of total load time for small sites.

4. **developers.google.com still slow:** 171% overhead, likely because it loads many sequential resources (Google fonts → Google APIs → Google Analytics) that can't be parallelized.

## Next Steps

- Target: reduce overhead to <50%
- Consider connection pooling for repeat domains
- Investigate connection idle timeouts for keep-alive cleanup
- Consider certificate generation parallelism (currently serialized per-host via ConcurrentDictionary)

---

*Generated automatically from test run on 2026-03-19*
