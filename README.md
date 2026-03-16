# Shmoxy - HTTP/HTTPS Proxy Server with TLS Termination

A .NET proxy server that terminates TLS connections and forwards traffic to target servers. Supports dynamic certificate generation for HTTPS interception.

## Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (for native development)
- [Nix](https://nixos.org/) (optional, for reproducible builds)

## Quick Start

### Using Nix Flakes (Recommended)

Enter the development shell:

```bash
nix develop
```

This provides a .NET SDK 10.0 environment with all dependencies configured.

#### Build the project

```bash
# From flake root
dotnet build src/shmoxy/shmoxy.csproj

# Or using nix build (outputs to $out)
nix build
```

#### Run tests

```bash
# Unit tests only (integration tests are skipped by default)
dotnet test src/tests/shmoxy.tests/shmoxy.tests.csproj

# All tests including integration
dotnet test --filter "FullyQualifiedName!~Integration"
```

#### Run the proxy server

```bash
# Default port 8080, Info logging
dotnet run --project src/shmoxy

# Custom port and log level
dotnet run -- -p 3000 -l Debug
```

### Using Nix Build Directly

Build without entering shell:

```bash
nix build .#shmoxy
```

The executable will be at `result/bin/shmoxy`.

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

## Development with Nix

The flake provides:
- Multi-platform support (aarch64-darwin, x86_64-darwin, aarch64-linux, x86_64-linux)
- Reproducible builds
- .NET SDK 10.0 matching the project target framework

To use on a different platform:

```bash
nix build --platform aarch64-darwin
```

## License

MIT
