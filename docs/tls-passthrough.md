# TLS Passthrough Architecture

## Problem: TLS Fingerprint Rejection

When shmoxy MITM-intercepts HTTPS traffic, it terminates TLS and re-establishes a new TLS connection to the upstream server. This changes the TLS fingerprint (JA3/JA4) — the upstream sees .NET's `SslStream` fingerprint instead of the browser's.

Services like Cloudflare use TLS fingerprinting for bot detection. When the fingerprint doesn't match a known browser, they reject the request — typically returning `400 Bad Request` or `403 Forbidden` with an HTML error page instead of the expected JSON response.

### Real-world example

Browsing `alpha.uipath.com` through shmoxy:

1. Browser loads the page (HTML/JS/CSS) — works fine, Cloudflare allows it
2. Auth library (`auth0-react`) calls `POST /identity_/connect/token` to exchange an auth code for a token
3. Cloudflare sees the proxy's TLS fingerprint on this connection and returns `400 Bad Request` with HTML
4. Auth library receives HTML instead of a JSON token — interprets it as auth failure
5. Auth library redirects to `/authorize` to get a new auth code
6. New auth code obtained, `POST /identity_/connect/token` called again → same 400
7. **Infinite loop**: authorize → callback → token (400) → authorize → ...

This was captured in the `bug-loop` inspection session: 22 failed token exchanges in ~20 seconds.

## Solution: TLS Passthrough

TLS passthrough tunnels a CONNECT request as raw TCP without terminating TLS. The browser's TLS bytes flow directly to the upstream server, preserving the original fingerprint.

### How it works

```
Normal MITM flow:
  Client ←TLS(fake cert)→ Proxy ←TLS(real cert)→ Server
  Proxy can read/modify HTTP. Server sees proxy's TLS fingerprint.

Passthrough flow:
  Client ←————— raw TCP bytes ——————→ Server
  Proxy pipes bytes bidirectionally. Server sees browser's TLS fingerprint.
  Proxy cannot see HTTP content (encrypted).
```

### Decision point

The passthrough decision happens in `HandleConnectAsync` when a `CONNECT host:port` request arrives — **before** any TLS handshake or HTTP request. At this point, the proxy only knows the hostname and port. It does not know the HTTP method, path, headers, or body.

This is a fundamental constraint: **inspecting requests requires terminating TLS (which changes the fingerprint), and preserving the fingerprint requires not terminating TLS (which prevents inspection).** These are mutually exclusive per-connection.

## Why path-level passthrough is not possible

A natural question: "Can we MITM most requests but passthrough specific paths like `/identity_/connect/token`?"

No, because:

1. **Path is only visible after MITM.** The CONNECT request only contains `host:port`. To see the HTTP path, the proxy must terminate TLS and read the decrypted request — at which point the fingerprint is already changed.

2. **Upstream TLS fingerprint is per-connection, not per-request.** Even if the proxy opens a "direct connection" for a specific request after reading it via MITM, that connection is still made by the proxy's .NET TLS stack. Cloudflare would see the same non-browser fingerprint and reject it.

3. **Cannot retroactively un-MITM.** Once the client has completed a TLS handshake with the proxy's fake certificate, there is no way to switch that specific connection to passthrough. The client is sending decrypted HTTP to the proxy — it can't suddenly start sending raw TLS to the upstream.

## Passthrough modes

### Static passthrough (implemented)

A configured list of hostnames/patterns that always bypass MITM:

```
PassthroughHosts: ["*.cloudflare.com", "id-alpha.uipath.com"]
```

Configured via the Proxy Config page or IPC API (`PUT /ipc/config`). Supports exact matches and glob patterns (`*.example.com`).

**Trade-off:** All traffic to matched hosts is invisible to inspection. This is acceptable for dedicated identity/auth domains (like `id-alpha.uipath.com`) but not for shared domains (like `alpha.uipath.com` where auth and app traffic share a hostname).

### Auto-detection (implemented)

Pluggable detectors analyze MITM-intercepted responses and suggest domains for passthrough:

- **Cloudflare Detector**: `Server: cloudflare` + `CF-RAY` header + 400/403 + HTML response when JSON was expected
- **WAF Detector**: 403 + HTML body containing WAF signatures (Akamai, AWS WAF, Imperva)
- **OAuth Token Detector**: POST to token endpoint (`/token`, `/connect/token`, etc.) returning non-JSON error

Suggestions appear on the Proxy Config page where users can accept (adds to passthrough list) or dismiss.

### Temporary passthrough (planned — see #108)

For shared domains where static passthrough is too broad:

1. MITM the connection normally — inspect everything
2. Detector fires when a request fails (e.g., token endpoint returns Cloudflare 400)
3. Domain is marked for temporary passthrough (next 1-2 CONNECT requests)
4. Client's auth library retries on a new connection — this time it's passed through
5. Token exchange succeeds (browser's real TLS fingerprint reaches Cloudflare)
6. Temporary passthrough expires — subsequent connections revert to MITM
7. Auth token is cached — the endpoint won't be called again until token refresh

This works because auth flows are retry-based and tokens are long-lived. The brief passthrough window lets the auth succeed without permanently blinding inspection.

## Configuration

### ProxyConfig properties

```json
{
  "PassthroughHosts": ["id-alpha.uipath.com", "*.cloudflare.com"],
  "EnabledDetectors": ["cloudflare", "waf", "oauth"]
}
```

### IPC API endpoints

| Endpoint | Description |
|---|---|
| `GET /ipc/config` | Get current config including PassthroughHosts |
| `PUT /ipc/config` | Update PassthroughHosts and EnabledDetectors |
| `GET /ipc/detectors` | List registered detectors and enabled state |
| `POST /ipc/detectors/{id}/enable` | Enable a detector |
| `POST /ipc/detectors/{id}/disable` | Disable a detector |
| `GET /ipc/detectors/suggestions` | Get active passthrough suggestions |
| `POST /ipc/detectors/suggestions/accept` | Accept suggestion (adds to PassthroughHosts) |
| `POST /ipc/detectors/suggestions/dismiss` | Dismiss suggestion (won't resurface) |
