# WebSocket Support — Implementation Plan

## Overview

Add WebSocket proxying, inspection, and filtering to shmoxy. WebSocket connections should be relayed bidirectionally through the proxy, with individual frames captured by the inspection system and displayed in the Inspector UI with protocol-level filtering.

## Architecture

### How WebSockets work through an HTTP proxy

1. Client sends `CONNECT host:port` (already handled by `ProxyServer.HandleConnectAsync`)
2. Proxy establishes TLS tunnel (already handled — MITM or passthrough)
3. Inside the tunnel, client sends HTTP upgrade request: `GET /path` with `Upgrade: websocket` + `Connection: Upgrade` + `Sec-WebSocket-Key`
4. Server responds `101 Switching Protocols` with `Upgrade: websocket` + `Sec-WebSocket-Accept`
5. After the handshake, both sides exchange WebSocket frames (text, binary, ping, pong, close)

**Key insight**: Steps 1-3 already flow through `HandleTunnelRequestsAsync` in `ProxyServer.cs`. The upgrade request is a normal HTTP request that shmoxy already intercepts. The change is: after detecting a 101 response, switch from HTTP request/response mode to WebSocket frame relay mode.

### Detection point

In `ProxyServer.HandleTunnelRequestsAsync` (line ~810), after receiving the upstream response:

```
if (response.StatusCode == 101 && IsWebSocketUpgrade(response))
{
    // Forward 101 to client, then switch to frame relay
    await HandleWebSocketRelayAsync(clientStream, upstreamStream, host, correlationId, ct);
    return; // Exit the HTTP request loop — connection is now WebSocket
}
```

## Implementation Steps

### Phase 1: WebSocket Frame Relay (core proxy functionality)

**New file: `src/shmoxy/server/WebSocketFrameReader.cs`**

Reads raw WebSocket frames from a stream per RFC 6455:
- Parse frame header: FIN, opcode (text/binary/close/ping/pong), mask, payload length
- Handle extended payload lengths (16-bit, 64-bit)
- Unmask client frames (client→server frames are always masked)
- Handle fragmented messages (FIN=0 continuation frames)

```csharp
public record WebSocketFrame
{
    public bool Fin { get; init; }
    public WebSocketOpcode Opcode { get; init; }  // Text=1, Binary=2, Close=8, Ping=9, Pong=10
    public byte[] Payload { get; init; }
    public bool IsMasked { get; init; }
}

public enum WebSocketOpcode : byte
{
    Continuation = 0, Text = 1, Binary = 2,
    Close = 8, Ping = 9, Pong = 10
}
```

**Modified: `src/shmoxy/server/ProxyServer.cs`**

Add `HandleWebSocketRelayAsync` method:
- After detecting 101, forward the upgrade response to the client
- Start two concurrent tasks: client→server relay and server→client relay
- Each task reads frames from one side, optionally inspects them, writes to the other side
- Handle close frames gracefully (forward close, wait for close response)
- Handle ping/pong transparently (forward as-is)

### Phase 2: WebSocket Inspection Events

**Modified: `src/shmoxy.shared/ipc/InspectionEvent.cs`**

Add new event types and fields:

```csharp
public record InspectionEvent
{
    // ... existing fields ...

    // New: WebSocket-specific fields
    public string? FrameType { get; init; }      // "text", "binary", "close", "ping", "pong"
    public string? Direction { get; init; }       // "client" or "server"
    public bool? IsWebSocket { get; init; }       // true for WS events
}
```

New event types:
- `"websocket_open"` — emitted when upgrade handshake succeeds. Method="GET", Url=upgrade path, StatusCode=101
- `"websocket_message"` — emitted for each data frame. Body=payload, FrameType="text"/"binary", Direction="client"/"server"
- `"websocket_close"` — emitted when close frame received. Body=close reason if present

**Modified: `src/shmoxy/server/hooks/InspectionHook.cs`**

Add methods:
```csharp
public Task OnWebSocketOpenAsync(string host, string path, string correlationId) { ... }
public Task OnWebSocketFrameAsync(string correlationId, WebSocketFrame frame, string direction) { ... }
public Task OnWebSocketCloseAsync(string correlationId, string? reason) { ... }
```

**Modified: `src/shmoxy/server/interfaces/IInterceptHook.cs`**

Add to interface:
```csharp
Task OnWebSocketOpenAsync(string host, string path, string correlationId) => Task.CompletedTask;
Task OnWebSocketFrameAsync(string correlationId, WebSocketFrame frame, string direction) => Task.CompletedTask;
Task OnWebSocketCloseAsync(string correlationId, string? reason) => Task.CompletedTask;
```

Use default interface methods so existing implementations (NoOpInterceptHook, PassthroughDetectorHook) don't need changes.

### Phase 3: Frontend Inspector Changes

**Modified: `src/shmoxy.frontend/services/InspectionDataService.cs`**

Update `ProcessEvent`:
- `websocket_open` → Create new `InspectionRow` with `IsWebSocket = true`, show upgrade URL
- `websocket_message` → Append to a frames list on the parent InspectionRow (grouped by CorrelationId)
- `websocket_close` → Mark the WebSocket row as closed, record close reason

New model:
```csharp
public class WebSocketFrameInfo
{
    public DateTime Timestamp { get; set; }
    public string Direction { get; set; }      // "client" → or "server" ←
    public string FrameType { get; set; }      // text, binary
    public string? Payload { get; set; }       // text content or "[binary N bytes]"
}
```

Add to `InspectionRow`:
```csharp
public bool IsWebSocket { get; set; }
public List<WebSocketFrameInfo> WebSocketFrames { get; set; } = new();
public bool WebSocketClosed { get; set; }
```

**Modified: `src/shmoxy.frontend/pages/Inspection.razor`**

Add protocol filter:
- New dropdown: "Protocol" → All, HTTP, WebSocket
- WebSocket rows show a distinct icon/badge (e.g., "WS" label)
- WebSocket rows show frame count instead of status code
- Clicking a WebSocket row opens the frame conversation view

**New component: `src/shmoxy.frontend/components/WebSocketDetail.razor`**

Conversation-style view of WebSocket frames:
- Client messages on one side, server messages on the other (like a chat)
- Text frames shown as formatted text (with JSON detection/formatting)
- Binary frames shown as hex dump or "[binary N bytes]"
- Timestamps on each frame
- Close frame shown as a system message at the bottom

### Phase 4: IPC & API Layer

**Modified: `src/shmoxy.api/Controllers/InspectionController.cs`**

No changes needed — the SSE stream already carries generic `InspectionEvent` objects. New event types will flow through automatically.

**Modified: `src/shmoxy.frontend/services/ApiClient.cs`**

No changes needed — `StreamInspectionEventsAsync` deserializes `InspectionEventDto` which will include new fields.

**Modified: `src/shmoxy.frontend/models/InspectionEventDto.cs`**

Add new optional fields:
```csharp
public string? FrameType { get; set; }
public string? Direction { get; set; }
public bool? IsWebSocket { get; set; }
```

## Test Plan

### Unit Tests (`src/tests/shmoxy.tests/`)

**New: `server/WebSocketFrameReaderTests.cs`**
- Parse text frame with small payload (< 126 bytes)
- Parse text frame with 16-bit extended payload (126-65535 bytes)
- Parse binary frame with 64-bit extended payload
- Parse masked client frame (unmask correctly)
- Parse unmasked server frame
- Parse close frame with status code and reason
- Parse ping frame
- Parse pong frame
- Parse fragmented message (multiple continuation frames)
- Handle incomplete frame (partial read)
- Write frame to stream (serialization)

**New: `server/hooks/InspectionHookWebSocketTests.cs`**
- WebSocket open event emitted with correct correlation ID
- WebSocket frame events emitted with direction and payload
- WebSocket close event emitted
- Disabled hook produces no WebSocket events
- Events preserve frame ordering

### E2E Tests (`src/tests/shmoxy.e2e/`)

**New: `WebSocketProxyTests.cs`**

Setup: Start ProxyServer + a simple WebSocket echo server (use `WebSocketListener` or ASP.NET `UseWebSockets`)

Tests:
- WebSocket connection through proxy succeeds (upgrade handshake)
- Text message relayed client→server and echoed back
- Binary message relayed correctly
- Multiple messages in sequence
- Close handshake completes cleanly
- Ping/pong forwarded transparently

**New: `WebSocketInspectionTests.cs`**

Setup: ProxyServer + InspectionHook + WebSocket echo server

Tests:
- WebSocket open event captured with upgrade URL
- Text frame events captured with correct direction ("client"/"server")
- Binary frame events captured
- Close event captured with reason
- CorrelationId consistent across open/frames/close for same connection
- Multiple concurrent WebSocket connections have distinct CorrelationIds

**New: `WebSocketInspectionSseTests.cs`**

Setup: ProxyServer + InspectionHook + IPC host + SSE consumer

Tests:
- WebSocket events survive SSE serialization round-trip
- Frame ordering preserved through SSE pipeline
- Large WebSocket payloads serialized correctly

### Frontend Tests (`src/tests/shmoxy.frontend.tests/`)

**New: `services/InspectionDataServiceWebSocketTests.cs`**
- ProcessEvent handles "websocket_open" → creates InspectionRow with IsWebSocket=true
- ProcessEvent handles "websocket_message" → appends frame to existing row
- ProcessEvent handles "websocket_close" → marks row as closed
- Protocol filter shows only WebSocket rows when selected
- Protocol filter shows only HTTP rows when selected

## File Summary

### New Files
| File | Description |
|------|-------------|
| `src/shmoxy/server/WebSocketFrameReader.cs` | RFC 6455 frame parser/writer |
| `src/shmoxy/server/models/WebSocketFrame.cs` | Frame data model + opcode enum |
| `src/shmoxy.frontend/components/WebSocketDetail.razor` | Conversation-style frame viewer |
| `src/tests/shmoxy.tests/server/WebSocketFrameReaderTests.cs` | Frame parser unit tests |
| `src/tests/shmoxy.tests/server/hooks/InspectionHookWebSocketTests.cs` | Hook unit tests |
| `src/tests/shmoxy.e2e/WebSocketProxyTests.cs` | Proxy relay e2e tests |
| `src/tests/shmoxy.e2e/WebSocketInspectionTests.cs` | Inspection e2e tests |
| `src/tests/shmoxy.e2e/WebSocketInspectionSseTests.cs` | SSE pipeline e2e tests |
| `src/tests/shmoxy.frontend.tests/services/InspectionDataServiceWebSocketTests.cs` | Frontend service tests |

### Modified Files
| File | Change |
|------|--------|
| `src/shmoxy/server/ProxyServer.cs` | Detect 101 upgrade, call WebSocket relay handler |
| `src/shmoxy/server/interfaces/IInterceptHook.cs` | Add WebSocket hook methods (default interface methods) |
| `src/shmoxy/server/hooks/InspectionHook.cs` | Implement WebSocket event emission |
| `src/shmoxy/server/hooks/InterceptHookChain.cs` | Forward WebSocket events through chain |
| `src/shmoxy.shared/ipc/InspectionEvent.cs` | Add WebSocket fields (FrameType, Direction, IsWebSocket) |
| `src/shmoxy.frontend/services/InspectionDataService.cs` | Process WebSocket events, group frames by connection |
| `src/shmoxy.frontend/pages/Inspection.razor` | Protocol filter dropdown, WS badge, frame count column |
| `src/shmoxy.frontend/components/InspectionDetail.razor` | Route to WebSocketDetail for WS rows |
| `src/shmoxy.frontend/models/InspectionEventDto.cs` | Add new optional fields |

## Implementation Order

1. **WebSocketFrameReader** + unit tests (pure parsing, no dependencies)
2. **ProxyServer relay** + e2e proxy tests (core functionality)
3. **InspectionHook WebSocket events** + hook unit tests + inspection e2e tests
4. **SSE pipeline** e2e tests (verify events flow through IPC)
5. **Frontend InspectionDataService** + service tests
6. **Frontend UI** (protocol filter, WS badge, WebSocketDetail component) + Playwright tests

## Risks & Considerations

- **Binary payloads**: Large binary WebSocket messages (e.g., video streaming) should not be fully captured in inspection events. Add a configurable max payload size for inspection (e.g., 1MB) and truncate with "[truncated N bytes]".
- **Long-lived connections**: WebSocket connections can last hours/days. The inspection system should handle this gracefully — emit frame events as they arrive, don't buffer the entire conversation.
- **Compression**: WebSocket permessage-deflate extension (RFC 7692) compresses frames. The proxy should forward compressed frames as-is. Inspection can optionally decompress for display.
- **Passthrough WebSockets**: WebSocket connections to passthrough hosts will tunnel without inspection (same as HTTPS passthrough). The `passthrough` event already covers this.
