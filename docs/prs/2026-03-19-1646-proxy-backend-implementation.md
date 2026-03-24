# PR: Proxy Backend Implementation (API + IPC)

**Created:** 2026-03-19
**Branch:** pr/proxy-backend-implementation
**Status:** Implementation Phase

## Description

Extend shmoxy with a separate ASP.NET Core API process that manages the proxy server lifecycle and communicates with it via IPC over Unix Domain Sockets.

## Architecture Overview

```
+----------------------------------+
|  shmoxy.api (ASP.NET Core)       |  <-- User-facing REST API
|  - Manages proxy lifecycle       |
|  - Exposes REST endpoints        |
|  - Plugin/hook management        |
|  - Request/response streaming    |
+----------------------------------+
|         IPC (HTTP over UDS)      |  <-- Unix Domain Socket
+----------------------------------+
|  shmoxy (Proxy Process)          |  <-- Existing proxy, mostly unchanged
|  - TCP proxy listener            |
|  - TLS interception              |
|  - Exposes internal control      |
|    API via Kestrel on UDS        |
+----------------------------------+
```

## IPC Mechanism: HTTP over Unix Domain Sockets

The proxy process embeds a lightweight Kestrel server listening on a UDS file (e.g., `/tmp/shmoxy-{guid}.sock`). This is not user-facing -- only the API process talks to it.

**Why HTTP-over-UDS:**
- Kestrel supports UDS natively -- no custom protocol needed
- Request routing, JSON serialization, middleware come for free
- Easy to test with `curl --unix-socket`
- The API process uses a standard `HttpClient` with `UnixDomainSocketEndPoint`

**UDS path convention:** The API process passes `--ipc-socket /tmp/shmoxy-{guid}.sock` to the proxy on spawn. The proxy creates the socket at that path.

## Project Structure

```
src/
├── shmoxy.slnx
├── shmoxy/                          # Existing proxy (modified)
│   ├── shmoxy.csproj                # Add: Microsoft.AspNetCore.App SDK
│   ├── Program.cs                   # Modified: also starts Kestrel on UDS
│   ├── server/
│   │   ├── ProxyServer.cs           # Mostly unchanged
│   │   ├── TlsHandler.cs
│   │   ├── ProxyHttpClient.cs
│   │   ├── interfaces/
│   │   │   └── IInterceptHook.cs
│   │   ├── hooks/
│   │   │   ├── NoOpInterceptHook.cs
│   │   │   ├── InterceptHookChain.cs
│   │   │   └── InspectionHook.cs    # NEW: request/response inspection
│   │   └── helpers/
│   │       └── RNGCryptoServiceProvider.cs
│   ├── ipc/                         # NEW: Internal control API
│   │   ├── ProxyControlApi.cs       # Minimal API endpoints on UDS
│   │   └── ProxyStateService.cs     # Singleton: exposes ProxyServer state
│   └── models/
│       ├── configuration/
│       │   └── ProxyConfig.cs
│       └── dto/
│
├── shmoxy.api/                      # NEW: ASP.NET Core API project
│   ├── shmoxy.api.csproj
│   ├── Program.cs                   # Kestrel on TCP (user-facing)
│   ├── server/
│   │   ├── ProxyProcessManager.cs   # Spawns/monitors proxy child process
│   │   └── interfaces/
│   │       └── IProxyProcessManager.cs
│   ├── ipc/
│   │   └── ProxyIpcClient.cs        # HttpClient over UDS to talk to proxy
│   ├── api/
│   │   ├── ProxyEndpoints.cs        # REST: /api/proxy/start, /stop, /status
│   │   ├── ConfigEndpoints.cs       # REST: /api/config
│   │   ├── HookEndpoints.cs         # REST: /api/hooks
│   │   ├── CertEndpoints.cs         # REST: /api/certs
│   │   └── InspectionEndpoints.cs   # REST: /api/inspect (SSE/WebSocket)
│   └── models/
│       ├── configuration/
│       │   └── ApiConfig.cs
│       └── dto/
│
├── shmoxy.shared/                   # NEW: Shared contracts library
│   ├── shmoxy.shared.csproj
│   └── ipc/
│       ├── IpcCommands.cs           # Shared DTOs for IPC messages
│       ├── ProxyStatus.cs           # Status model
│       └── HookDescriptor.cs        # Hook registration model
│
└── tests/
    ├── shmoxy.tests/
    ├── shmoxy.api.tests/            # NEW
    └── shmoxy.e2e/
```

## Internal Control API (Proxy-side, on UDS)

Endpoints the proxy exposes only on the Unix domain socket:

| Method | Path                        | Description                                      |
|--------|-----------------------------|--------------------------------------------------|
| GET    | /ipc/status                 | Health: `{ isListening, port, uptime, activeConnections }` |
| POST   | /ipc/shutdown               | Graceful shutdown (triggers CancellationToken)    |
| GET    | /ipc/config                 | Current config                                   |
| PUT    | /ipc/config                 | Update runtime config (log level, etc.)          |
| GET    | /ipc/hooks                  | List active hooks with on/off status             |
| POST   | /ipc/hooks/{id}/enable      | Enable a hook                                    |
| POST   | /ipc/hooks/{id}/disable     | Disable a hook                                   |
| GET    | /ipc/certs/root.pem         | Root CA in PEM format                            |
| GET    | /ipc/certs/root.der         | Root CA in DER format                            |
| GET    | /ipc/inspect/stream         | SSE stream of intercepted requests/responses     |
| POST   | /ipc/inspect/enable         | Turn on request/response inspection              |
| POST   | /ipc/inspect/disable        | Turn off inspection                              |

## External REST API (API-side, user-facing)

Exposed on a normal TCP port (e.g., `http://localhost:5000`):

| Method   | Path                        | Description                                    |
|----------|-----------------------------|------------------------------------------------|
| POST     | /api/proxy/start            | Start the proxy process                        |
| POST     | /api/proxy/stop             | Stop the proxy process                         |
| GET      | /api/proxy/status           | Proxy health + stats (proxied from IPC)        |
| GET/PUT  | /api/config                 | Get/update proxy config                        |
| GET      | /api/hooks                  | List hooks                                     |
| POST     | /api/hooks/{id}/enable      | Enable hook                                    |
| POST     | /api/hooks/{id}/disable     | Disable hook                                   |
| GET      | /api/certs/root.pem         | Download root CA                               |
| GET      | /api/inspect/stream         | WebSocket/SSE for live traffic inspection      |
| POST     | /api/inspect/enable         | Enable inspection                              |
| POST     | /api/inspect/disable        | Disable inspection                             |

## Process Lifecycle

```
1. User starts:
   dotnet run --project shmoxy.api -- --port 5000 --proxy-port 8080

2. API process starts Kestrel on TCP :5000

3. On POST /api/proxy/start (or auto-start):
   a. ProxyProcessManager spawns:
      dotnet run --project shmoxy -- --port 8080 --ipc-socket /tmp/shmoxy-{guid}.sock
   b. Proxy process starts TCP listener on :8080
   c. Proxy process starts Kestrel on UDS /tmp/shmoxy-{guid}.sock
   d. API process connects HttpClient to UDS
   e. API polls GET /ipc/status until healthy

4. On POST /api/proxy/stop:
   a. API sends POST /ipc/shutdown to proxy over UDS
   b. Proxy gracefully drains connections and exits
   c. ProxyProcessManager detects exit, cleans up socket file

5. If proxy crashes:
   a. ProxyProcessManager detects Process.Exited event
   b. Can auto-restart or report via /api/proxy/status
```

## Plugin/Hook System

Hooks are registered at startup from configuration but can be enabled/disabled at runtime:

```csharp
// In shmoxy.shared/ipc/HookDescriptor.cs
public record HookDescriptor
{
    public string Id { get; init; }        // e.g., "file-cache"
    public string Name { get; init; }      // e.g., "File Cache Hook"
    public string Type { get; init; }      // "builtin" | "script"
    public string? ScriptPath { get; init; } // for Lua/script hooks
    public bool Enabled { get; init; }
}
```

The `InterceptHookChain` is extended with an `EnabledHookChain` wrapper that checks `Enabled` before calling each hook. Hooks are loaded once but toggled on/off without reconstruction.

Script-based hooks (Lua, etc.) are a future plugin system -- the architecture accommodates it via `ScriptPath` and a `ScriptInterceptHook` implementation, but not built in the first pass.

## Request/Response Inspection

Implemented as a special `IInterceptHook`:

```csharp
public class InspectionHook : IInterceptHook
{
    private readonly Channel<InspectionEvent> _channel;
    private bool _enabled;

    // When enabled, writes to channel; when disabled, pass-through
    // The IPC /inspect/stream endpoint reads from the channel and SSE-streams it
}
```

- **Off by default** (no performance overhead when disabled)
- When enabled, writes intercepted request/response data to a `Channel<T>`
- The IPC endpoint streams from that channel via SSE
- The API process proxies the SSE stream to the user

## Key Design Decisions

1. **Proxy uses Generic Host** with `IHostedService` for clean lifecycle management. Both the proxy TCP listener and IPC API run as hosted services.

2. **The `--ipc-socket` CLI argument** is how the API process tells the proxy where to create the UDS. Avoids hardcoded paths and allows multiple instances.

3. **Shared library (`shmoxy.shared`)** contains only DTOs and contracts -- no logic. Both projects reference it.

4. **The existing `IsRequestToProxyItself()` info page stays** as a convenience for users hitting the proxy directly in a browser. The API is the "real" control plane.

5. **Changes to existing `shmoxy` project are minimal:**
   - Add `--ipc-socket` CLI option
   - Add `ipc/` folder with `ProxyControlApi.cs`, `ProxyStateService.cs`, and `IpcHostedService.cs`
   - Add `InspectionHook` to hooks
   - Refactor `Program.cs` to use Generic Host pattern

## Notes

### Phase 1 Summary (✅ Complete)

**Delivered:**
- IPC control API with 10 endpoints (status, config, hooks, inspection, certs, shutdown)
- Unix socket and HTTP admin port support
- API key authentication for HTTP (auto-generated, logged on startup)
- Generic Host refactoring with `ProxyHostedService` and `IpcHostedService`
- Shared `ShmoxyHost` class for test consistency
- 38 passing tests (10 unit + 28 e2e)
- Zero compiler warnings
- Nix build verified

**Ready for:** Merge to main

### Phase 2 (Future PR)

- The proxy can still be run standalone (without the API) for simple use cases -- the IPC socket is optional.
- Multiple proxy instances can be managed by a single API process by spawning multiple child processes with different socket paths.
- The inspection feature uses `System.Threading.Channels` for backpressure-safe event streaming.
- Integration tests reuse the same host initialization logic as `Program.cs` via `ShmoxyHost` class.
- All compiler warnings are treated as errors (zero warnings policy).

### Phase 2 (Planned)

**Proxy Modes:**
- **Local**: API spawns proxy as child process, communicates via Unix socket
- **Remote**: Proxy runs on separate server, registers with API via HTTP
- **Direct HTTP**: Proxy exposes admin endpoints over HTTP (no IPC)

**Architecture Decisions:**
- Single API can manage multiple proxies (local + remote)
- Remote proxies authenticate via API keys
- Health checks via polling with exponential backoff
- Read-only operations on remote proxies (no shutdown/restart)

**Security Considerations:**
- Local IPC: Unix socket permissions (owner-only access)
- Remote HTTP: API key authentication, HTTPS required
- Certificates: Root CA download requires authentication
