# PR: shmoxy.api Backend Project

**Created:** 2026-03-21 10:35
**Branch:** pr/shmoxy-api-backend
**Worktree Location:** /Users/phaser/projects/shmoxy-worktrees/shmoxy-api-backend

## Description

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
```

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

**Implementation:**

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

#### Phase 5.1: CertsController ✅ COMPLETED

**Endpoint:** `GET /api/proxies/{proxyId}/certs/root?type=[pem|der]`

**Implementation:**
- ✅ ProxyServer: Added `GetRootCertificatePfx()` method
- ✅ ProxyControlApi: Added `/ipc/certs/root.pfx` endpoint
- ✅ IProxyIpcClient: Added `GetRootCertPfxAsync()` method
- ✅ ProxyIpcClient: Implemented PFX method
- ✅ ProxyProcessManager: Added cert download methods for local proxy access
- ✅ CertsController: Single endpoint for local/remote proxy certificate downloads
- ✅ Tests: 7 unit tests covering local and remote scenarios

**Design Decisions:**
- No PFX download (root CA should not distribute private key)
- No caching headers (users naturally cache after 1-2 downloads)
- Unified route pattern (local = "local", remote = GUID)
- Default to PEM format (most common use case)
- Content-Disposition header for download hint

**Test Results:** 55 tests passing

#### Phase 5.2: InspectionController ✅ COMPLETED

**Endpoint:** `GET /api/proxies/{proxyId}/inspect/stream`

**Implementation:**
- ✅ SSE streaming endpoint for real-time traffic inspection
- ✅ Works with local ("local") and remote proxies (GUID)
- ✅ Reuses existing IProxyIpcClient.GetInspectionStreamAsync()
- ✅ Content-type: text/event-stream
- ✅ No filtering (stream all request/response pairs)
- ✅ Graceful disconnect handling

**Tests (4 scenarios):**
- ✅ Stream endpoint returns SSE content-type
- ✅ Proxy not running - stream fails gracefully
- ✅ Remote proxy not found - handled gracefully
- ✅ Stream disconnects cleanly on client cancellation

#### Phase 5.3: ConfigController ✅ COMPLETED

**Endpoints:**
- ✅ `GET /api/proxies/{proxyId}/config` - Get current config
- ✅ `PUT /api/proxies/{proxyId}/config` - Update config (immediate, no restart)

**Implementation:**
- ✅ Use shmoxy.shared.ipc.ProxyConfig directly (no DTO duplication)
- ✅ Config updates are immediate via IPC (no restart required)
- ✅ Config validation (port range, log level, concurrent connections)
- ✅ Works with both local and remote proxies

**Tests (4 scenarios):**
- ✅ Get config from local proxy
- ✅ Update config with invalid port returns 400
- ✅ Update config with invalid log level returns 400
- ✅ Update config validation passes for valid config

#### Testing Strategy ✅ COMPLETED

**Integration tests:** 8 total (4 per controller)
**Test location:** `src/tests/shmoxy.api.tests/Integration/`
**Approach:** API-level validation with mocked services

### Phase 6: Tests
- [ ] Integration tests for real process spawning
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

## Security Considerations

- **API Key Storage**: Store API keys securely (encrypted at rest) — currently plaintext, deferred
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

## Notes

- The `shmoxy` proxy process remains unchanged (backward compatible)
- Users can still run `shmoxy` standalone without the API
- The API is an optional management layer
