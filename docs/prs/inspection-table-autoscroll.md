# PR: Inspection table with auto-scroll

**Created:** 2026-03-23 16:30:18
**Branch:** pr/inspection-table-autoscroll
**Worktree Location:** /Users/phaser/projects/shmoxy-worktrees/inspection-table-autoscroll

## Description

Redesign the Inspection page to show a fixed-height scrollable table of proxied requests with columns `#`, `Method`, `URL`, `Duration`. The table auto-scrolls to the bottom as new requests arrive via SSE streaming. Auto-scroll pauses when the user scrolls up to inspect rows, and can be resumed. Includes Playwright E2E tests for column verification and scroll behavior.

## Status

- [ ] Development in progress
- [ ] Tests added/updated
- [ ] Documentation updated
- [ ] Ready for review
- [ ] Merged

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

### ApiClient.cs — SSE streaming method
- Add `StreamInspectionEventsAsync(proxyId, CancellationToken)` returning `IAsyncEnumerable<InspectionEventDto>`
- Opens GET to `/api/proxies/{proxyId}/inspect/stream` with `ResponseHeadersRead`
- Parses `data: {...}` SSE lines into `InspectionEventDto` objects

### InspectionEventDto.cs (new)
- Frontend DTO mirroring the SSE JSON shape (avoids adding `shmoxy.shared` dependency)
- Fields: `Timestamp`, `EventType`, `Method`, `Url`, `StatusCode`

### InspectionPageTests.cs (new) — E2E tests
- `InspectionPage_HasCorrectColumns` — verifies `#`, `Method`, `URL`, `Duration` column headers
- `InspectionPage_HasScrollableContainer` — verifies fixed-height div with overflow-y exists

## Files Created
- `src/shmoxy.frontend/models/InspectionEventDto.cs`
- `src/tests/shmoxy.frontend.tests/InspectionPageTests.cs`

## Files Modified
- `src/shmoxy.frontend/pages/Inspection.razor`
- `src/shmoxy.frontend/wwwroot/js/app.js`
- `src/shmoxy.frontend/services/ApiClient.cs`

## Implementation Plan

### Context
The Inspection page currently shows a static `FluentDataGrid` with manual refresh. The user wants a fixed-height table that streams requests in real-time with auto-scroll that can be paused/resumed, plus E2E tests. Existing filters stay but are not modified in this PR.

### Step 1. Create `InspectionEventDto.cs`
**File:** `src/shmoxy.frontend/models/InspectionEventDto.cs`
- Record with: `Timestamp`, `EventType`, `Method`, `Url`, `StatusCode`
- Mirrors `shmoxy.shared.ipc.InspectionEvent` JSON shape (frontend has no reference to shared project)

### Step 2. Add SSE streaming to `ApiClient.cs`
**File:** `src/shmoxy.frontend/services/ApiClient.cs`
- Add `StreamInspectionEventsAsync(string proxyId = "local", CancellationToken ct)` → `IAsyncEnumerable<InspectionEventDto>`
- Use `HttpClient.GetAsync` with `HttpCompletionOption.ResponseHeadersRead`
- Read response stream line-by-line, parse `data: {...}` lines with `JsonSerializer.Deserialize`
- Keep existing methods unchanged

### Step 3. Add auto-scroll JS interop to `app.js`
**File:** `src/shmoxy.frontend/wwwroot/js/app.js`
- `window.inspectionAutoScroll` object with:
  - `init(elementId)` — attach scroll listener, track `_isAtBottom` state (true when within 50px of bottom)
  - `scrollToBottom(elementId)` — set `scrollTop = scrollHeight`
  - `isAtBottom(elementId)` — return bool
  - `dispose(elementId)` — remove listener

### Step 4. Rewrite `Inspection.razor`
**File:** `src/shmoxy.frontend/pages/Inspection.razor`

**Markup:**
- Keep existing filter controls (search, method dropdown) — no changes to filter logic
- Replace `FluentDataGrid` with a fixed-height `<div id="inspection-scroll-container">` (500px, `overflow-y: auto`)
- HTML `<table>` with `<thead>` columns: `#`, `Method`, `URL`, `Duration`
- `<tbody>` rows from `List<InspectionRow>` (inner class: Id, Method, Url, Duration?, Timestamp)
- "Resume auto-scroll" button shown when user has scrolled up
- Empty state: "Start the proxy to see requests" when proxy isn't running / no data

**Code-behind:**
- `InspectionRow` inner class: `int Id`, `string Method`, `string Url`, `TimeSpan? Duration`, `DateTime Timestamp`
- `OnInitializedAsync`: start SSE stream consumption via `_ = ConsumeStreamAsync()`
- `ConsumeStreamAsync`: iterate `ApiClient.StreamInspectionEventsAsync("local", cts.Token)`, pair request/response events (FIFO queue), add rows, cap at 1000, call `await InvokeAsync(StateHasChanged)`, then conditionally auto-scroll
- `OnAfterRenderAsync(firstRender)`: call `JS.InvokeVoidAsync("inspectionAutoScroll.init", "inspection-scroll-container")`
- `IAsyncDisposable`: cancel CTS, call `inspectionAutoScroll.dispose`
- Duration pairing: `Queue<(int rowIndex, DateTime timestamp)>` of unpaired requests; on "response" event, dequeue and set `Duration = response.Timestamp - request.Timestamp`
- Handle SSE connection failure gracefully (catch, show empty state)

**Scoped CSS:**
- Fixed height container, table styling using FluentUI design tokens for theme consistency

### Step 5. Create E2E tests
**File:** `src/tests/shmoxy.frontend.tests/InspectionPageTests.cs`
- `[Collection("Frontend")]` class using `FrontendTestFixture`
- `InspectionPage_HasCorrectColumns`: navigate to `/inspection`, wait for render, assert `<th>` elements contain "#", "Method", "URL", "Duration"
- `InspectionPage_HasScrollableContainer`: assert `#inspection-scroll-container` exists with appropriate overflow style

## Testing

- Playwright E2E: column headers verified, scroll container verified
- Manual: start proxy, send requests through it, confirm table populates with auto-scroll, scroll up to pause, click resume

## Verification
1. `dotnet build src/shmoxy.slnx` — 0 warnings, 0 errors
2. `dotnet test src/tests/shmoxy.frontend.tests/` — all tests pass (existing + new)
3. Manual: start proxy, send traffic, verify table populates, auto-scrolls, pauses on scroll-up, resumes on button click

## Notes

- Duration pairing uses a FIFO queue (first unpaired request matched to next response). Concurrent requests may mismatch — a correlation ID in `InspectionEvent` would be a future improvement.
- Row cap at 1000 prevents unbounded memory growth.
- `StateHasChanged` called via `InvokeAsync` since SSE loop runs in a background task.
- Existing search/method filters kept in the UI but not modified in this PR.
- When proxy isn't running, shows "Start the proxy to see requests" empty state.
