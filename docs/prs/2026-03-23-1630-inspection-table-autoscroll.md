# PR: Inspection table with auto-scroll

**Created:** 2026-03-23 16:30:18
**Branch:** pr/inspection-table-autoscroll
**Worktree Location:** /Users/phaser/projects/shmoxy-worktrees/inspection-table-autoscroll

## Description

Redesign the Inspection page to show a fixed-height scrollable table of proxied requests with columns `#`, `Method`, `URL`, `Duration`. The table auto-scrolls to the bottom as new requests arrive via SSE streaming. Auto-scroll pauses when the user scrolls up to inspect rows, and can be resumed. Includes Playwright E2E tests for column verification and scroll behavior.

Also fixes multiple bugs discovered during integration testing that prevented the proxy from working end-to-end.

## Changes Made

### Inspection.razor — Complete rewrite
- Replace `FluentDataGrid` with a plain HTML `<table>` inside a fixed-height (500px) `<div>` with `overflow-y: auto`
- Columns: `#` (sequential row number), `Method`, `URL`, `Duration`
- Duration computed by pairing request/response `InspectionEvent`s from SSE stream
- Shows "-" for duration until response arrives
- Auto-scroll to bottom on new rows via JS interop
- Auto-scroll pauses when user scrolls up; "Resume auto-scroll" button appears
- Implements `IAsyncDisposable` for cleanup
- Caps row list at 1000 entries to prevent memory growth

### app.js — Auto-scroll JS interop
- `inspectionAutoScroll.init(elementId)` — attach scroll listener tracking user position
- `inspectionAutoScroll.scrollToBottom(elementId)` — scroll to bottom
- `inspectionAutoScroll.isAtBottom(elementId)` — returns bool
- `inspectionAutoScroll.dispose(elementId)` — cleanup

### ApiClient.cs — SSE streaming method + error handling
- Add `StreamInspectionEventsAsync(proxyId, CancellationToken)` returning `IAsyncEnumerable<InspectionEventDto>`
- Opens GET to `/api/proxies/{proxyId}/inspect/stream` with `ResponseHeadersRead`
- Parses `data: {...}` SSE lines into `InspectionEventDto` objects
- Add `EnsureSuccessOrThrowWithBody` for better error extraction from JSON error responses

### InspectionEventDto.cs (new)
- Frontend DTO mirroring the SSE JSON shape (avoids adding `shmoxy.shared` dependency)
- Fields: `Timestamp`, `EventType`, `Method`, `Url`, `StatusCode`

### ProxyInstanceStateDto.cs (new)
- Frontend DTO for proxy state responses

### InspectionPageTests.cs (new) — E2E tests
- `InspectionPage_HasCorrectColumns` — verifies `#`, `Method`, `URL`, `Duration` column headers
- `InspectionPage_HasScrollableContainer` — verifies fixed-height div with overflow-y exists

### ProxyConfigPageTests.cs (new) — E2E tests
- `ProxyConfigPage_ShowsStoppedByDefault` — verifies initial stopped state
- `ProxyConfigPage_SaveFailsWhenProxyStopped` — verifies error when saving without running proxy
- `ProxyConfigPage_HasCorrectFormFields` — verifies form fields exist
- `ProxyConfigPage_StartProxy_StatusChangesToRunning` — full start/stop lifecycle test

## Bug Fixes

### 1. HTTPS MITM interception not calling hooks
**Root cause:** `HandleConnectAsync` in `ProxyServer.cs` performed TLS termination but then `ProxyTunnelAsync` did raw bidirectional byte copy without parsing HTTP or calling the `_interceptor` hook.
**Fix:** Replaced `ProxyTunnelAsync` with `HandleTunnelRequestsAsync` that reads decrypted HTTP requests from the SSL stream, calls `_interceptor.OnRequestAsync()` and `_interceptor.OnResponseAsync()`, then forwards to upstream.

### 2. Inspection not auto-enabled
**Root cause:** `InspectionHook.Enabled` defaults to `false` and the frontend had no toggle to enable it.
**Fix:** `InspectionController.GetLocalStream()` now calls `EnableInspectionAsync()` before starting the SSE stream.

### 3. IPC client not using Unix socket in InspectionController
**Root cause:** `InspectionController.GetLocalStream()` created a plain `HttpClient` with `BaseAddress = http://localhost` instead of connecting via the Unix domain socket. Same bug in `GetRootCertPemAsync` and `GetRootCertDerAsync`.
**Fix:** Added `GetIpcClient()` to `IProxyProcessManager` interface, exposed the socket-connected IPC client from `ProxyProcessManager`. Controller and cert methods now use the properly-connected client.

### 4. SSE stream timeout after 60 seconds
**Root cause:** `ProxyIpcClient.GetInspectionStreamAsync()` used `cts.CancelAfter(IpcTimeouts.Streaming)` (60s) which killed long-lived SSE connections.
**Fix:** Removed the timeout; streaming now uses only the caller's cancellation token. Also set `HttpClient.Timeout = Timeout.InfiniteTimeSpan` on the socket client.

### 5. JSON enum serialization mismatch
**Root cause:** `ProxyProcessState` enum serialized as integer (0, 1, 2...) but frontend expected string ("Running", "Stopped").
**Fix:** Added `JsonStringEnumConverter` to API controller JSON options in `Program.cs`.

### 6. ProxyProcessManager discarding injected IPC client
**Root cause:** Constructor had `IProxyIpcClient _` (discard), so mock health checks in tests were never used. Also meant the injected client was never available for other callers.
**Fix:** Store as `_injectedIpcClient` when `ProxyIpcSocketPath` is configured. `GetOrCreateSocketIpcClient()` returns injected client first, falls back to creating socket client.

### 7. WaitForHealthyAsync leaking OperationCanceledException
**Root cause:** `Task.Delay` in the health check loop wasn't wrapped in try/catch, so when the 15s CTS fired, the exception propagated as "task was canceled" instead of returning false.
**Fix:** Wrapped both health check call and delay in try/catch blocks that return false on cancellation.

### 8. ProxyHostedService swallowing startup errors
**Root cause:** `_proxyTask = _server.StartAsync(...)` fire-and-forget pattern — if `_listener.Start()` threw (e.g., port already in use), the error was captured in `_proxyTask` but never observed.
**Fix:** Added `ContinueWith(OnlyOnFaulted)` to log errors. Also added `SocketException` catch in `ProxyServer.StartAsync()` with descriptive port-binding error message.

### 9. Test port conflicts
**Root cause:** `ApiConfig.ProxyPort` defaults to 8080, no override in test fixture.
**Fix:** `FrontendTestFixture` now allocates a dynamic port and passes it via `--ApiConfig:ProxyPort`.

### 10. Unit test exception type mismatch
**Root cause:** After fixing WaitForHealthyAsync, `StartAsync` now throws `TimeoutException` instead of `TaskCanceledException`.
**Fix:** Updated test to `Assert.ThrowsAsync<TimeoutException>`.

## Files Created
- `src/shmoxy.frontend/models/InspectionEventDto.cs`
- `src/shmoxy.frontend/models/ProxyInstanceStateDto.cs`
- `src/tests/shmoxy.frontend.tests/InspectionPageTests.cs`
- `src/tests/shmoxy.frontend.tests/ProxyConfigPageTests.cs`

## Files Modified
- `src/shmoxy/server/ProxyServer.cs` — MITM interception with hook calls
- `src/shmoxy/server/ProxyHostedService.cs` — Error observation for fire-and-forget task
- `src/shmoxy.api/Controllers/InspectionController.cs` — Use socket IPC client, auto-enable inspection
- `src/shmoxy.api/ipc/ProxyIpcClient.cs` — Remove streaming timeout
- `src/shmoxy.api/server/IProxyProcessManager.cs` — Add `GetIpcClient()` method
- `src/shmoxy.api/server/ProxyProcessManager.cs` — Store injected IPC client, fix WaitForHealthyAsync, fix cert methods, infinite timeout for streaming
- `src/shmoxy.api/Program.cs` — Add `JsonStringEnumConverter`
- `src/shmoxy.api/shmoxy.api.csproj` — Build targets
- `src/shmoxy.frontend/pages/Inspection.razor` — Complete rewrite with SSE streaming table
- `src/shmoxy.frontend/pages/ProxyConfig.razor` — UI improvements
- `src/shmoxy.frontend/services/ApiClient.cs` — SSE streaming, better error handling
- `src/shmoxy.frontend/wwwroot/js/app.js` — Auto-scroll JS interop
- `src/shmoxy.frontend/extensions/FluentUiBlazorConfiguration.cs` — Configuration updates
- `src/shmoxy.frontend/models/FrontendProxyConfig.cs` — Model updates
- `src/tests/shmoxy.api.tests/server/ProxyProcessManagerTests.cs` — Fix exception type
- `src/tests/shmoxy.frontend.tests/FrontendTestFixture.cs` — Dynamic proxy port
- `src/tests/shmoxy.frontend.tests/shmoxy.frontend.tests.csproj` — Dependencies

## Testing

- Unit tests: 10 shmoxy.tests + 63 shmoxy.api.tests — all pass
- Playwright E2E: column headers verified, scroll container verified, proxy start/stop lifecycle verified
- Manual: start proxy, send HTTP/HTTPS traffic, verify table populates with auto-scroll

## Notes

- Duration pairing uses a FIFO queue (first unpaired request matched to next response). Concurrent requests may mismatch — a correlation ID in `InspectionEvent` would be a future improvement.
- Row cap at 1000 prevents unbounded memory growth.
- `StateHasChanged` called via `InvokeAsync` since SSE loop runs in a background task.
- HTTPS interception now uses `Connection: close` per tunneled request (no HTTP keep-alive within MITM tunnel).
