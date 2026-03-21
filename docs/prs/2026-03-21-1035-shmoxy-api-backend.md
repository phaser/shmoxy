# PR: shmoxy.api Backend Project

**Created:** 2026-03-21
**Branch:** pr/shmoxy-api-backend
**Status:** Planned

## Summary

Create the `shmoxy.api` backend project that manages proxy instances (local and remote) and exposes a user-facing REST API.

## Motivation

The current implementation provides:
- ✅ IPC control API in the proxy process (`shmoxy`)
- ✅ Unix socket and HTTP admin endpoints
- ✅ API key authentication for HTTP

What's missing:
- ❌ Centralized management of multiple proxy instances
- ❌ Remote proxy registration and management
- ❌ User-facing REST API with higher-level operations

## Scope

### In Scope

1. **Create shmoxy.api project**
   - ASP.NET Core Web API
   - References `shmoxy.shared` for IPC contracts

2. **ProxyProcessManager**
   - Spawn local proxy as child process
   - Monitor process health
   - Graceful shutdown
   - Track socket paths and ports

3. **ProxyIpcClient**
   - HttpClient wrapper for Unix socket communication
   - HttpClient wrapper for HTTP admin API (with API key auth)
   - Strongly-typed methods for all IPC endpoints

4. **Remote Proxy Support**
   - Register remote proxies via configuration or API
   - HTTP-based communication with API key authentication
   - Health monitoring with exponential backoff

5. **User-Facing REST API**
   - Proxy lifecycle management
   - Configuration management
   - Certificate downloads
   - Inspection event streaming

### Out of Scope (Future PRs)

- WebSocket-based real-time updates
- Multi-tenant authentication/authorization
- Proxy load balancing
- Advanced metrics and alerting
- UI/dashboard

## Design

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      shmoxy.api                              │
│  ┌────────────────┐  ┌──────────────┐  ┌─────────────────┐  │
│  │ ProxyController│  │AdminController│  │ CertsController │  │
│  └───────┬────────┘  └──────┬───────┘  └────────┬────────┘  │
│          │                  │                    │           │
│  ┌───────▼──────────────────▼────────────────────▼────────┐  │
│  │              ProxyIpcClient                             │  │
│  │  (HTTP + Unix Socket, API Key Auth)                     │  │
│  └───────┬──────────────────┬─────────────────────────────┘  │
│          │                  │                                 │
│  ┌───────▼────────┐  ┌──────▼──────────┐                     │
│  │ProcessManager  │  │RemoteProxyRegistry│                   │
│  └───────┬────────┘  └─────────────────┘                     │
└──────────┼────────────────────────────────────────────────────┘
           │
           │ Unix Socket / HTTP
           ▼
    ┌──────────────┐     ┌──────────────┐
    │ shmoxy (local)│    │shmoxy (remote)│
    └──────────────┘     └──────────────┘
```

### Proxy Modes

| Mode | Description | Communication |
|------|-------------|---------------|
| **Local** | Spawned by API as child process | Unix socket |
| **Remote** | Runs on separate server, registered with API | HTTP + API key |
| **Direct HTTP** | Proxy exposes admin endpoints over HTTP | HTTP + API key |

### API Endpoints

#### Proxy Management
- `GET /api/proxies` - List all proxies
- `GET /api/proxies/{id}` - Get proxy details
- `POST /api/proxies` - Register remote proxy
- `DELETE /api/proxies/{id}` - Unregister proxy
- `POST /api/proxies/local` - Spawn local proxy
- `POST /api/proxies/{id}/shutdown` - Shutdown proxy (local only)

#### Configuration
- `GET /api/proxies/{id}/config` - Get proxy config
- `PUT /api/proxies/{id}/config` - Update proxy config

#### Certificates
- `GET /api/certs/root.pem` - Download root CA (proxies to selected proxy)

#### Inspection
- `GET /api/proxies/{id}/inspect/stream` - SSE event stream
- `POST /api/proxies/{id}/inspect/enable` - Enable inspection
- `POST /api/proxies/{id}/inspect/disable` - Disable inspection

#### Health
- `GET /api/health` - API health
- `GET /api/proxies/{id}/health` - Proxy health

### Data Models

```csharp
public class ProxyInstance
{
    public string Id { get; set; }           // UUID
    public string Name { get; set; }         // User-assigned name
    public ProxyMode Mode { get; set; }      // Local, Remote, DirectHttp
    public string? SocketPath { get; set; }  // For local proxies
    public string? AdminUrl { get; set; }    // For remote proxies
    public string? ApiKey { get; set; }      // For HTTP auth
    public ProxyStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

public enum ProxyMode
{
    Local,
    Remote,
    DirectHttp
}

public enum ProxyStatus
{
    Starting,
    Running,
    Stopped,
    Error,
    Unreachable
}
```

## Implementation Plan

### Phase 1: Project Setup
- [ ] Create `src/shmoxy.api/shmoxy.api.csproj`
- [ ] Add ASP.NET Core Web API template
- [ ] Reference `shmoxy.shared`
- [ ] Basic health endpoint

### Phase 2: ProxyIpcClient
- [ ] HttpClient factory for Unix sockets
- [ ] HttpClient factory for HTTP with API key
- [ ] Strongly-typed methods for all IPC endpoints
- [ ] Connection pooling and retry logic

### Phase 3: Local Proxy Management
- [ ] ProxyProcessManager service
- [ ] Spawn proxy with `Process.Start()`
- [ ] Monitor process health
- [ ] Graceful shutdown
- [ ] Cleanup on API shutdown

### Phase 4: Remote Proxy Registry
- [ ] RemoteProxyRegistry service
- [ ] Configuration-based registration
- [ ] Dynamic registration API
- [ ] Health monitoring with backoff

### Phase 5: REST API Controllers
- [ ] ProxiesController (lifecycle management)
- [ ] ConfigController (configuration)
- [ ] CertsController (certificate downloads)
- [ ] InspectionController (SSE streaming)

### Phase 6: Tests
- [ ] Unit tests for ProxyIpcClient
- [ ] Unit tests for ProxyProcessManager
- [ ] Integration tests for REST endpoints
- [ ] E2E tests with real proxy child processes

## Security Considerations

- **API Key Storage**: Store API keys securely (encrypted at rest)
- **HTTPS Required**: Remote proxy communication must use HTTPS
- **Key Rotation**: Support API key rotation for remote proxies
- **Rate Limiting**: Protect against DoS on registration endpoints
- **Audit Logging**: Log proxy registration/unregistration events

## Configuration

```json
{
  "Proxies": {
    "Local": {
      "AutoStart": true,
      "Port": 0,
      "CertStoragePath": "~/.local/share/shmoxy"
    },
    "Remote": [
      {
        "Name": "prod-us-east",
        "AdminUrl": "https://prod-east.example.com:9090",
        "ApiKey": "xxx"
      }
    ]
  },
  "HealthCheck": {
    "IntervalSeconds": 30,
    "TimeoutSeconds": 5,
    "MaxRetries": 3
  }
}
```

## Dependencies

- .NET 10.0
- ASP.NET Core
- System.CommandLine (for CLI if needed)
- Microsoft.Extensions.Http (for HttpClient factory)

## Notes

- The `shmoxy` proxy process remains unchanged (backward compatible)
- Users can still run `shmoxy` standalone without the API
- The API is an optional management layer
