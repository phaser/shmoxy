# Shmoxy - HTTP/HTTPS Intercepting Proxy

A .NET proxy server that terminates TLS connections and forwards traffic to target servers. Supports dynamic certificate generation for HTTPS interception.

## Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download)

## Quick Start

### Try it out

Build a distribution and run:

```bash
./scripts/dist.sh
./scripts/start.sh
```

The web UI will be available at `http://localhost:5000` and the proxy listens on port `8080`.

To customize ports:

```bash
./scripts/start.sh --port 3000 --proxy-port 9090
```

### Development

#### Build the project

```bash
dotnet build src/shmoxy/shmoxy.csproj
```

#### Run tests

```bash
# Unit tests only (integration tests are skipped by default)
dotnet test src/tests/shmoxy.tests/shmoxy.tests.csproj

# All tests including integration
dotnet test --filter "FullyQualifiedName!~Integration"
```

#### Run for development

```bash
# Run the API with Blazor frontend
dotnet run --project src/shmoxy.api

# Or run only the proxy engine (no API or frontend)
dotnet run --project src/shmoxy -- --port 8080
```

## Blazor Frontend

The project includes a Blazor Server-based web UI for managing proxies and inspecting requests. It's served from the `shmoxy.api` project as an embedded Razor Class Library.

### Frontend Features

- **Dashboard** - Overview of proxy status and quick access to features
- **Proxy Configuration** - Configure host, port, HTTPS interception settings
- **Request Inspection** - View intercepted requests/responses with headers and bodies
- **Theme Toggle** - Switch between dark and light modes (persists in localStorage)

## Proxy Usage

Configure your HTTP client to use the proxy:

```bash
# Test with curl (HTTP tunnel)
curl -x http://localhost:8080 https://news.ycombinator.com

# Test with curl (HTTPS through proxy)
curl -x https://localhost:8080 https://news.ycombinator.com
```

## CLI Options

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--port` | `-p` | Listening port | 8080 |
| `--cert` | | Path to TLS certificate (future use) | - |
| `--key` | | Path to TLS private key (future use) | - |
| `--log-level` | `-l` | Logging: Debug, Info, Warn, Error | Info |
| `--inspection-capture-limit` | | Maximum retained body-preview bytes; `0` disables body capture | 1048576 |
| `--ipc-socket` | | Optional Unix socket for the control/inspection API | - |
| `--admin-port` | | Optional authenticated TCP control API port | - |

The proxy engine has no frontend dependency. Without a control endpoint it runs
with a no-op interception hook and streams traffic directly. Supplying an IPC
socket or admin port enables the optional inspection and breakpoint adapters
used by the API/frontend.

## Architecture

```
┌─────────────┐     ┌──────────────────┐     ┌──────────────┐
│   Client    │────▶│  Proxy Server    │────▶│  Target Site │
│             │◀────│ (TLS termination)│◀────│              │
└─────────────┘     └──────────────────┘     └──────────────┘
                          │
                          ▼
                 [InterceptHook] - Future decoding
```

## Components

- **ProxyServer** - Core proxy logic with CONNECT and HTTP handling
- **TlsHandler** - Dynamic certificate generation with SNI support
- **InterceptHook** - Extensible request/response interception interface
- **ProxyHttpClient** - Forwarding client for tunnel-based requests

## Testing

### Unit Tests

```bash
dotnet test --filter "FullyQualifiedName!~Integration"
```

Tests cover:
- Server startup/shutdown
- TLS certificate generation and caching
- Hook chain execution order

### Integration Tests

Skip by default (requires network access). To run manually, remove the `[Fact(Skip = "...")]` attribute from `ProxyTests.cs`.

## License

MIT
