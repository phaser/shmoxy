# PR: shmoxy-api-backend

**Created:** 2026-03-21 10:51:14
**Branch:** pr/shmoxy-api-backend
**Worktree Location:** /Users/phaser/projects/shmoxy-worktrees/shmoxy-api-backend

## Description

Create the `shmoxy.api` backend project that manages proxy instances (local and remote) and exposes a user-facing REST API.

## Status

- [x] Phase 1: Project Setup
- [ ] Phase 2: ProxyIpcClient
- [ ] Phase 3: Local Proxy Management
- [ ] Phase 4: Remote Proxy Registry
- [ ] Phase 5: REST API Controllers
- [ ] Phase 6: Tests
- [ ] Development in progress
- [ ] Tests added/updated
- [ ] Documentation updated
- [ ] Ready for review
- [ ] Merged

## Implementation Plan

### Phase 1: Project Setup ✅ COMPLETED
- [x] Create `src/shmoxy.api/shmoxy.api.csproj`
- [x] Add ASP.NET Core Web API template
- [x] Reference `shmoxy.shared`
- [x] Basic health endpoint (`GET /api/health`)
- [x] ApiConfig model with Port, ProxyPort, ProxyIpcSocketPath, AutoStartProxy

### Phase 2: ProxyIpcClient ✅ COMPLETED
- [x] Move `ProxyConfig` to `shmoxy.shared` for contract sharing
- [x] Create `IpcTimeouts` with 5 configurable timeout levels (Small/Medium/Long/VeryLong/Streaming)
- [x] Create `IProxyIpcClient` interface with 13 methods
- [x] Create `ProxyIpcClient` implementation with:
  - Mode-agnostic HttpClient (works with UDS or HTTP)
  - Retry with exponential backoff (3 retries, base 100ms, max 5s)
  - Timeout per operation using `IpcTimeouts`
  - SSE parser for inspection stream
  - Logging for retries, timeouts, connection errors
- [x] Create `ServiceCollectionExtensions` for DI registration
  - `AddProxyIpcClient()` - generic registration
  - `AddUnixSocketIpcClient()` - for local proxy
  - `AddHttpIpcClient()` - for remote proxy with API key auth
- [x] Update `Program.cs` to register IPC client
- [x] Unit tests for ProxyIpcClient (17 tests)
- [x] Unit tests for ServiceCollectionExtensions (3 tests)
- [x] All 59 tests passing (21 API + 10 unit + 28 e2e)
- [x] Zero compiler warnings

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
- [ ] Unit tests for ProxyProcessManager
- [ ] Integration tests for REST endpoints
- [ ] E2E tests with real proxy child processes

## Design Decisions

### ProxyIpcClient Architecture

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Move ProxyConfig to shared | Yes | Avoids circular dependency, clean contract sharing |
| SSE reconnection | No (KISS) | Let caller handle reconnection logic |
| Logging | Yes | Essential for debugging retries/timeouts |
| Test mocking | Inline HttpMessageHandler | No extra dependencies |
| Retry strategy | Exponential backoff, 3 retries | Standard pattern, handles transient failures |
| Timeout strategy | 5 configurable levels | Flexibility without complexity |
| Mode awareness | No (mode-agnostic) | Single client, DI handles configuration |

### Timeout Levels

```csharp
Small = 2s      // Health checks, simple queries
Medium = 5s     // Config operations
Long = 10s      // Hook operations
VeryLong = 30s  // Certificate downloads
Streaming = 60s // SSE stream connection timeout
```

### Retry Logic

- 3 retries maximum
- Exponential backoff: base 100ms, max 5s delay
- Only retry transient errors (network failures, 5xx, timeouts)
- Never retry 4xx client errors

## Architecture

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

## Notes

- The `shmoxy` proxy process remains unchanged (backward compatible)
- Users can still run `shmoxy` standalone without the API
- The API is an optional management layer

---
*This document was auto-generated by scripts/new-pr.sh*
