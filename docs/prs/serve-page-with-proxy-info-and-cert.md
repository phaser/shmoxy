# PR: Serve Page with Proxy Info and Certificate Download

**Created:** 2026-03-17
**Branch:** feature/serve_page_with_proxy_info_and_cert

## Description

When a user navigates to `http://proxy_ip:proxy_port` in their browser, the proxy should serve an HTML info page instead of trying to forward the request. This page provides:

1. Proxy server information (name, version, listening address/port, uptime)
2. A downloadable link to the root CA certificate (PEM format)
3. Step-by-step instructions for installing and trusting the certificate on all major platforms and browsers

This is essential for usability -- without trusting the proxy's root CA, browsers will reject every HTTPS connection through the proxy.

## Status

- [ ] Development in progress
- [ ] Tests added/updated
- [ ] Documentation updated
- [ ] Ready for review
- [ ] Merged

## Prerequisites / Bug Fixes Included

Before implementing the info page, two issues in the current codebase must be addressed:

### P1: Root CA is generated but never used to sign per-host certificates

Currently `TlsHandler.GenerateCertificateForHost()` calls `request.CreateSelfSigned()` instead of using the root CA as the issuer. Per-host certs must be signed by the root CA so that trusting the root CA is sufficient for all proxied domains.

**Fix:** Use `request.Create(_rootCert, ...)` instead of `request.CreateSelfSigned(...)` in `GenerateCertificateForHost`. Add SAN (Subject Alternative Name) extension to per-host certs for browser compatibility.

### P2: No mechanism to detect "request is for the proxy itself"

`HandleHttpRequestAsync` always extracts a host from the request and forwards it. There is no check for whether the request targets the proxy's own address.

**Fix:** Add a self-request detection check at the top of `HandleHttpRequestAsync`. When the target host+port matches the proxy's listening address, route to the info page handler instead of forwarding.

## Implementation Plan

### Phase 1: Fix Certificate Chain (TlsHandler.cs)

**Goal:** Make the root CA certificate actually useful -- per-host certs must be signed by it.

1. **Expose the root certificate publicly** -- add a `public X509Certificate2 RootCertificate => _rootCert;` property on `TlsHandler` so `ProxyServer` can export it.

2. **Fix `GenerateCertificateForHost`** to sign with the root CA:
   - Replace `request.CreateSelfSigned(now, now.AddYears(1))` with `request.Create(_rootCert, now, now.AddYears(1), serialNumber)`.
   - Add `SubjectAlternativeName` extension with DNS name for the host.
   - Keep the private key attached by calling `.CopyWithPrivateKey(privateKey)` on the issued cert.

3. **Add `ExportRootCertificatePem()` method** on `TlsHandler`:
   - Returns the root CA as a PEM-encoded string (`-----BEGIN CERTIFICATE-----...`).
   - Uses `_rootCert.ExportCertificatePem()` (.NET 7+ API).

4. **Update tests** in `TlsHandlerTests.cs`:
   - Verify per-host certs are signed by the root CA (check `Issuer` field).
   - Verify the PEM export method returns valid PEM content.
   - Verify SAN extension is present on per-host certs.

### Phase 2: Self-Request Detection (ProxyServer.cs)

**Goal:** Distinguish "proxy this request to another server" from "the user is browsing to the proxy itself."

1. **Add a `IsRequestForSelf` method** in `ProxyServer`:
   ```
   Inputs: parsed host, parsed port
   Returns: true if (host is localhost/127.0.0.1/0.0.0.0/::1 AND port == ListeningPort)
            OR (host matches the machine's hostname AND port == ListeningPort)
            OR (port == ListeningPort AND no absolute URL in the request, i.e. relative path only)
   ```
   The last condition handles browsers that send `GET / HTTP/1.1` with `Host: proxy_ip:proxy_port` -- since the proxy received a non-absolute-URL request with itself as the Host, it's clearly addressed to the proxy.

2. **Insert the check** at the top of `HandleHttpRequestAsync`, after host/port parsing:
   ```csharp
   if (IsRequestForSelf(host, port))
   {
       await HandleSelfRequestAsync(client, relativePath);
       return;
   }
   ```

3. **Unit tests** in `ProxyServerTests.cs`:
   - `GET /` with `Host: localhost:PORT` => serves info page.
   - `GET http://localhost:PORT/` => serves info page.
   - `GET http://example.com/` => forwards normally (not self).
   - `GET /shmoxy.pem` => serves certificate download.

### Phase 3: Info Page Handler (new file: InfoPageHandler.cs)

**Goal:** Serve the HTML info page and the certificate download endpoint.

This is a new class to keep `ProxyServer.cs` focused on proxying. It is responsible for generating HTTP responses for self-addressed requests.

#### Routes

| Path | Response |
|------|----------|
| `/` or `/index.html` | HTML info page |
| `/shmoxy-ca.pem` | Root CA certificate download (PEM format) |
| `/shmoxy-ca.crt` | Root CA certificate download (DER format, for Windows/macOS) |
| anything else | 404 Not Found |

#### Class Design

```csharp
public class InfoPageHandler
{
    private readonly ProxyConfig _config;
    private readonly TlsHandler _tlsHandler;
    private readonly DateTime _startTime;

    public InfoPageHandler(ProxyConfig config, TlsHandler tlsHandler, DateTime startTime);

    /// <summary>
    /// Handles a request directed at the proxy itself.
    /// Writes an HTTP response to the client stream.
    /// </summary>
    public async Task HandleAsync(TcpClient client, string method, string path);
}
```

#### HTML Info Page Content

The info page will be a **self-contained single HTML file** with inline CSS (no external dependencies). Content sections:

1. **Header** -- "Shmoxy Proxy" with version
2. **Proxy Info Table** -- listening port, uptime, root CA subject, root CA expiry
3. **Certificate Download** -- prominent download buttons for both `.pem` and `.crt` formats
4. **Installation Instructions** -- tabbed/accordion UI with sections for:

   **Operating Systems:**
   - **Windows** -- `certutil -addstore -f "ROOT" shmoxy-ca.crt` or MMC snap-in steps
   - **macOS** -- `sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain shmoxy-ca.crt` or Keychain Access GUI steps
   - **Linux (Debian/Ubuntu)** -- copy to `/usr/local/share/ca-certificates/` and run `sudo update-ca-certificates`
   - **Linux (RHEL/Fedora)** -- copy to `/etc/pki/ca-trust/source/anchors/` and run `sudo update-ca-trust`

   **Browsers:**
   - **Chrome** (uses OS store on Windows/macOS; needs NSS on Linux): `certutil -d sql:$HOME/.pki/nssdb -A -t "C,," -n "Shmoxy Proxy CA" -i shmoxy-ca.pem`
   - **Firefox** (has its own store): Settings > Privacy & Security > Certificates > View Certificates > Import, or `certutil` with Firefox profile path
   - **Edge** (uses OS store, same as Windows/macOS instructions)

5. **Footer** -- link to project repo (if applicable)

#### HTTP Response Construction

Since the proxy uses raw TCP sockets, responses must be manually constructed:

```csharp
private async Task SendHtmlResponseAsync(TcpClient client, string html)
{
    var body = Encoding.UTF8.GetBytes(html);
    var header = $"HTTP/1.1 200 OK\r\n" +
                 $"Content-Type: text/html; charset=utf-8\r\n" +
                 $"Content-Length: {body.Length}\r\n" +
                 $"Connection: close\r\n\r\n";
    // write header + body to stream
}

private async Task SendFileDownloadAsync(TcpClient client, string filename, string contentType, byte[] data)
{
    var header = $"HTTP/1.1 200 OK\r\n" +
                 $"Content-Type: {contentType}\r\n" +
                 $"Content-Disposition: attachment; filename=\"{filename}\"\r\n" +
                 $"Content-Length: {data.Length}\r\n" +
                 $"Connection: close\r\n\r\n";
    // write header + data to stream
}
```

#### Testing (InfoPageHandlerTests.cs)

- `HandleAsync_RootPath_ReturnsHtmlWithProxyInfo` -- verify response contains proxy info
- `HandleAsync_PemDownload_ReturnsPemCertificate` -- verify PEM content and Content-Type
- `HandleAsync_CrtDownload_ReturnsDerCertificate` -- verify DER content and Content-Disposition
- `HandleAsync_UnknownPath_Returns404` -- verify 404 response
- `HandleAsync_HtmlContainsInstallInstructions` -- verify platform-specific instructions present
- Integration test: start server, HTTP GET to `http://localhost:PORT/`, verify HTML response

### Phase 4: Wire It All Together (ProxyServer.cs)

1. **Add `InfoPageHandler` field** to `ProxyServer`, initialized in constructor.
2. **Track start time** in `ProxyServer` for uptime display.
3. **Expose `TlsHandler` internally** (or pass it to `InfoPageHandler` via constructor).
4. **Call `InfoPageHandler.HandleAsync`** from the self-request detection branch in `HandleHttpRequestAsync`.

### Phase 5: Build Verification & Final Checks

1. `dotnet build src/shmoxy.slnx` -- must pass
2. `dotnet test src/tests/shmoxy.tests/` -- all tests must pass
3. `nix build .#shmoxy` -- Nix build must pass
4. Manual smoke test: start proxy, open `http://localhost:8080/` in browser, verify page renders and cert downloads work

## Changes Made

| File | Change |
|------|--------|
| `src/shmoxy/TlsHandler.cs` | Expose root cert, fix cert signing chain, add PEM/DER export, add SAN extension |
| `src/shmoxy/ProxyServer.cs` | Add self-request detection, wire InfoPageHandler, track start time |
| `src/shmoxy/InfoPageHandler.cs` | **NEW** -- HTML info page generation, cert download endpoints, HTTP response helpers |
| `src/tests/shmoxy.tests/TlsHandlerTests.cs` | Tests for cert chain, PEM export, SAN |
| `src/tests/shmoxy.tests/ProxyServerTests.cs` | Tests for self-request detection |
| `src/tests/shmoxy.tests/InfoPageHandlerTests.cs` | **NEW** -- Tests for all info page routes |

## Testing

- **Unit tests:** Each new/modified class has dedicated test file per project convention
- **Integration test:** Full round-trip test -- start server, HTTP GET to self, verify HTML + cert download
- **Manual test:** Browser verification that the page renders correctly and certs are downloadable
- **Build verification:** `dotnet build`, `dotnet test`, `nix build .#shmoxy`

## Notes

### Design Decisions

1. **Self-contained HTML (no external CSS/JS):** The proxy has no static file serving infrastructure. Embedding everything in a single HTML string avoids complexity. The page uses inline CSS with a clean, modern design. No JavaScript frameworks needed -- just CSS tabs/accordions for the instruction sections.

2. **Both PEM and DER formats:** PEM (`.pem`) is the standard on Linux and for command-line tools. DER (`.crt`) is preferred by Windows and macOS for double-click installation. Offering both maximizes usability.

3. **InfoPageHandler as a separate class:** Keeps `ProxyServer.cs` focused on proxying. The handler is testable in isolation without needing a running TCP server.

4. **Self-detection heuristic:** Checking `localhost`/`127.0.0.1`/`::1` + port match covers the vast majority of cases. The relative-path fallback (`GET /` with matching Host header) catches the browser case where the user types the proxy address directly.

### Risks & Mitigations

- **False positive self-detection:** A proxy request for `http://localhost:8080/` (when the proxy runs on 8080) would be caught as "self" even if the user genuinely wants to proxy a request to localhost:8080 on a different machine. This is an acceptable trade-off since proxying to yourself is not a real use case. If needed, a `/__shmoxy__/` prefix could be used instead, but that hurts discoverability.

- **Large HTML string in code:** The HTML template will be ~200-300 lines. It could be embedded as a resource file, but for simplicity the first iteration will use a string literal or interpolated string builder. Can be refactored to an embedded resource later if it grows unwieldy.

### Follow-up Work (Out of Scope)

- HTTPS info page (serving over TLS when user visits `https://proxy:port/`) -- requires TLS negotiation before detecting the path
- Configurable branding/custom HTML template
- JSON API endpoint (`/api/info`) for programmatic access
- Certificate auto-renewal notification

---
*This document tracks the implementation plan for the serve-page-with-proxy-info-and-cert feature.*
