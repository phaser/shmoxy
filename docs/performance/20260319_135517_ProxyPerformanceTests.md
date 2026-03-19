# Performance Test Results - ProxyPerformanceTests

**Date:** 2026-03-19 13:55:17  
**Branch:** feature_make-proxy-muti-threaded  
**Test Class:** `shmoxy.e2e.ProxyPerformanceTests`

## Test Summary

| Test | Status | Duration |
|------|--------|----------|
| Baseline_NoProxy_TotalLoadTime | ✅ Passed | 4s |
| Proxy_WithProxy_TotalLoadTime | ❌ Failed | 755ms |

## Test Configuration

- **Test Sites:**
  - https://finance.yahoo.com
  - https://www.reddit.com
  - https://arstechnica.com

- **Timeout:** 60000ms per page load
- **Wait Condition:** `WaitUntilState.Load`

## Results

### Baseline_NoProxy_TotalLoadTime (PASSED)

**Total Load Time:** 3278ms for 3 sites

**Individual Site Load Times:**
- finance.yahoo.com: 1321ms
- www.reddit.com: 352ms
- arstechnica.com: 1604ms

**Artifacts Location:** 
```
src/tests/shmoxy.e2e/bin/Debug/net10.0/playwright_run_db083d89/
```

### Proxy_WithProxy_TotalLoadTime (FAILED)

**Status:** Failed with certificate validation error

**Error:**
```
Microsoft.Playwright.PlaywrightException : net::ERR_CERT_AUTHORITY_INVALID at https://finance.yahoo.com/
```

**Error Details:**
- The proxy's TLS certificate is not trusted by the browser
- Authentication failed during CONNECT request to finance.yahoo.com:443
- This is a known issue with the proxy's certificate handling

**Stack Trace:**
```
at Microsoft.Playwright.Core.Frame.GotoAsync(String url, FrameGotoOptions options)
at shmoxy.e2e.ProxyPerformanceTests.Proxy_WithProxy_TotalLoadTime()
```

**Artifacts Location:**
```
src/tests/shmoxy.e2e/bin/Debug/net10.0/playwright_run_e283bd46/
```

## Issues Identified

1. **Certificate Validation Failure:** The proxy server's generated certificates are not trusted by Playwright's browser instance, causing HTTPS requests to fail with `ERR_CERT_AUTHORITY_INVALID`.

2. **Missing Certificate Configuration:** The test fixture needs to properly configure the browser to trust the proxy's root certificate, or the proxy needs to use a properly signed certificate.

## Recommendations

1. **Fix Certificate Trust:** Configure the Playwright browser context to trust the proxy's root CA certificate
2. **Re-run Tests:** Once certificate issue is resolved, re-run both tests to get complete performance comparison
3. **Calculate Overhead:** Compare proxy vs baseline to determine performance overhead percentage

## Next Steps

- Fix certificate validation in `ProxyTestFixture` or `PlaywrightTestFixture`
- Re-run performance tests after fix
- Document performance overhead metrics

---

*Generated automatically from test run on 2026-03-19*
