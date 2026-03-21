# PR: shmoxy-api-backend

**Created:** 2026-03-21 10:51:14
**Branch:** pr/shmoxy-api-backend
**Worktree Location:** /Users/phaser/projects/shmoxy-worktrees/shmoxy-api-backend

## Description

Create the `shmoxy.api` backend project that manages proxy instances (local and remote) and exposes a user-facing REST API.

## Status

- [x] Phase 1: Project Setup
- [x] Phase 2: ProxyIpcClient
- [x] Phase 3: Local Proxy Management
- [x] Phase 4: Remote Proxy Registry
- [ ] Phase 5: REST API Controllers
- [ ] Phase 6: Tests
- [x] Development in progress
- [x] Tests added/updated
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

### Phase 3: Local Proxy Management ✅ COMPLETED

**Design Decisions:**
- Proxy binary path: Config can specify path, default to "shmoxy" in PATH
- Validate binary exists before starting: Yes, fail fast
- AutoStartProxy: Start in IHostedService.StartAsync() if enabled
- Integration tests: Mark with `[Trait("Category", "Integration")]`, no skipping

**Implementation Plan:**

1. **State Tracking** ✅
   - [x] Create `ProxyInstanceState.cs` with state enum (Starting/Running/Stopping/Stopped/Crashed)
   - [x] Track: Id, State, ProcessId, SocketPath, Port, StartedAt, StoppedAt, ExitReason

2. **ProxyProcessManager Service** ✅
   - [x] Create `IProxyProcessManager.cs` interface
   - [x] Create `ProxyProcessManager.cs` implementation
   - [x] Spawn proxy via `Process.Start()` with redirected stdout/stderr
   - [x] Health polling (100ms interval, 5s timeout)
   - [x] Graceful shutdown via IPC `ShutdownAsync()` (10s timeout)
   - [x] Force kill if doesn't exit gracefully
   - [x] Socket file cleanup on exit
   - [x] State change events via `EventHandler<ProxyInstanceState>`

3. **REST API Controller** ✅
   - [x] Create `ProxiesController.cs`
   - [x] `GET /api/proxies/local` - Get proxy state
   - [x] `POST /api/proxies/local/start` - Start proxy
   - [x] `POST /api/proxies/local/stop` - Stop proxy
   - [x] `POST /api/proxies/local/restart` - Restart proxy

4. **Auto-Start on API Startup** ✅
   - [x] Create `ProxyHostedService.cs`
   - [x] Start proxy if `ApiConfig.AutoStartProxy = true`
   - [x] Register as hosted service in DI

5. **Configuration Updates** ✅
   - [x] Add `ProxyBinaryPath` to `ApiConfig` (default: "shmoxy")
   - [x] Add `AutoStartProxy` to `ApiConfig` (default: false)

6. **Tests** ✅
   - [x] Unit tests for ProxyProcessManager (12 tests)
   - [x] Unit tests for ProxiesController (9 tests)
   - [ ] Integration tests for real process spawning (marked with Trait)

7. **Integration** ✅
   - [x] Register services in Program.cs
   - [x] Add controller routing
   - [ ] Update shmoxy.api.csproj with shmoxy reference

### Phase 4: Remote Proxy Registry ✅ COMPLETED

**Implementation:**

1. **Database Layer** ✅
   - [x] SQLite database in user data directory (`~/.local/share/shmoxy-api/proxies.db`)
   - [x] EF Core DbContext with auto-migration
   - [x] RemoteProxy entity with Id, Name, AdminUrl, ApiKey, Status, LastHealthCheck, CreatedAt, UpdatedAt

2. **Service Layer** ✅
   - [x] IRemoteProxyRegistry interface with CRUD + TestConnectivity
   - [x] RemoteProxyRegistry implementation with EF Core
   - [x] Connectivity testing during registration

3. **Health Monitoring** ✅
   - [x] RemoteProxyHealthMonitor background service (IHostedService)
   - [x] 30-second health check interval (configurable)
   - [x] Exponential backoff on failures (5s base, 5min max)
   - [x] Status updates (Unknown/Healthy/Unhealthy/Unreachable)

4. **REST API Controller** ✅
   - [x] GET /api/proxies/remote - List all remote proxies
   - [x] GET /api/proxies/remote/{id} - Get proxy by ID
   - [x] POST /api/proxies/remote - Register new proxy (with connectivity test)
   - [x] PUT /api/proxies/remote/{id} - Update proxy (API key rotation)
   - [x] DELETE /api/proxies/remote/{id} - Unregister proxy
   - [x] POST /api/proxies/remote/{id}/health - Force health check

5. **Configuration** ✅
   - [x] ConnectionString in ApiConfig (defaults to user data directory)
   - [x] HealthCheckIntervalSeconds (default: 30)

6. **Tests** ✅
   - [x] RemoteProxyRegistryTests (10 tests, 1 skipped)
   - [x] Integration with existing test suite (47 passing tests)

**Security Notes:**
- ⚠️ API keys stored in plaintext (encryption deferred to later phase)
- API key returned only on POST (creation), never on GET/PUT
- Connectivity validation on registration prevents invalid entries

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
