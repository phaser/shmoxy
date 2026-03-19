# Initial Proxy Implementation - Documentation

## Overview

This document describes the implementation of a proxy server with TLS termination for the shmoxy project. The implementation supports HTTP/HTTPS traffic interception, dynamic certificate generation, and extensible request/response hooks.

## Components Implemented

### 1. Core Files Created

#### `src/shmoxy/TlsHandler.cs`
- Handles TLS termination and dynamic certificate generation
- Supports SNI (Server Name Indication) for multi-domain proxying
- Caches generated certificates to avoid redundant creation
- Generates self-signed root CA certificate on startup
- Creates per-host certificates signed by the root CA

**Key Features:**
- Dynamic certificate generation using RSA 2048-bit keys
- SHA-256 signature algorithm
- Subject Alternative Name (SAN) extension for SNI support
- Certificate caching with thread-safe access
- Automatic cleanup on disposal

#### `src/shmoxy/InterceptHook.cs`
- Defines the interface for request/response interception
- Provides extensible hook system for middleware chains

**Types:**
- `IInterceptHook` - Interface for custom interceptors
- `NoOpInterceptHook` - Default no-operation implementation
- `InterceptHookChain` - Middleware chain supporting multiple hooks

**Request/Response Objects:**
- `InterceptedRequest` - Contains method, URL, headers, body
- `InterceptedResponse` - Contains status code, headers, body

#### `src/shmoxy/ProxyHttpClient.cs`
- Handles HTTP requests through proxy tunnels
- Implements CONNECT method for HTTPS tunneling

**Key Features:**
- Automatic tunnel establishment via CONNECT
- Content-Length parsing for response body handling
- Stream-based reading for efficient memory usage

#### `src/shmoxy/Program.cs`
- CLI entry point with command-line argument parsing
- Uses System.CommandLine library

**Arguments:**
- `-p, --port <int>` - Listening port (default: 8080)
- `--cert <path>` - Path to TLS certificate file
- `--key <path>` - Path to TLS private key file
- `-l, --log-level <level>` - Logging verbosity (Debug|Info|Warn|Error)

#### `src/shmoxy/ProxyServer.cs`
- Main proxy server implementation
- Handles both CONNECT and regular HTTP requests

**Request Handling:**
- Detects CONNECT vs HTTP methods from first bytes
- For CONNECT: establishes TLS tunnel with dynamic certificate
- For HTTP: parses request, forwards through tunnel
- Bidirectional stream copying for transparent proxying

### 2. Test Files Created

#### `src/tests/shmoxy.tests/ProxyTests.cs`
Unit tests covering:
- Server startup and shutdown
- TLS handler certificate generation
- Certificate caching behavior
- Hook chain execution order
- No-op interceptor pass-through

Integration test placeholder for site forwarding (skipped by default due to network requirements).

## Architecture Diagram

```
┌─────────────┐     ┌──────────────────┐     ┌──────────────┐
│   Client    │────▶│  Proxy Server    │────▶│  Target Site │
│             │◀────│ (TLS termination)│◀────│              │
└─────────────┘     └──────────────────┘     └──────────────┘
                          │
                          ▼
                 [InterceptHook] - Future decoding
```

## File Structure

```
src/shmoxy/
├── Program.cs             # CLI entry point
├── ProxyServer.cs         # Core proxy logic
├── TlsHandler.cs          # TLS termination & cert generation
├── InterceptHook.cs       # Interception interface
├── ProxyHttpClient.cs     # Forwarding client
├── shmoxy.csproj          # Project file (updated)

src/tests/shmoxy.tests/
└── ProxyTests.cs          # Unit and integration tests
```

## Usage Examples

### Start proxy server on default port 8080:
```bash
dotnet run --project src/shmoxy
```

### Start with custom port:
```bash
dotnet run --project src/shmoxy -- -p 3000
```

### Set logging level to debug:
```bash
dotnet run --project src/shmoxy -- -l Debug
```

### Test with curl (HTTP):
```bash
curl -x http://localhost:8080 https://news.ycombinator.com
```

### Test with curl (HTTPS through proxy):
```bash
curl -x https://localhost:8080 https://news.ycombinator.com
```

## Testing Strategy

### Unit Tests
Run all unit tests:
```bash
dotnet test src/tests/shmoxy.tests/ProxyTests.cs
```

### Integration Tests
Integration tests are marked with `[Fact(Skip = "...")]` and require network access. Run manually by removing the Skip attribute:
```csharp
[Fact]  // Remove Skip for manual testing
public async Task Integration_ShouldForwardRequestsToTargetSites() { ... }
```

## Open Questions & Future Work

1. **Certificate Validation**: Currently accepts all certificates for proxying. Consider adding proper validation mode for production use.

2. **Dynamic Cert Generation vs Provided Certs**: The implementation currently uses dynamic cert generation. Support for provided certs (--cert/--key flags) is in place but the TlsHandler always generates its own root CA.

3. **Response Interception Hook**: Currently stubbed out. Future work to implement response decoding/modification capabilities.

4. **Similarity Comparison Tests**: The PR plan mentions comparing responses from three sites with 95% similarity threshold. This would require HTML parsing and normalization logic.

## Dependencies

- `System.CommandLine` (v2.0.0-beta4.22272.1) - CLI argument parsing
- Built-in .NET libraries for networking and TLS

## Known Limitations

1. **TLS Certificate Trust**: Clients must trust the dynamically generated root CA or disable certificate validation
2. **No HTTP/2 Support**: Currently only HTTP/1.1
3. **Single-threaded Stream Copying**: Bidirectional copying could be optimized with better async patterns
4. **Memory Buffer Sizes**: Fixed 8KB buffers may not be optimal for all use cases
