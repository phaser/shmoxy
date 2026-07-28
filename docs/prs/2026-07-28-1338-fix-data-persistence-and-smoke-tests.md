# PR: fix-data-persistence-and-smoke-tests

**Created:** 2026-07-28 13:38:48
**Branch:** pr/fix-data-persistence-and-smoke-tests
**Worktree Location:** /Users/cristian.bidea/projects/shmoxy/../shmoxy-worktrees/fix-data-persistence-and-smoke-tests

## Description

Persist API data dir in Docker, clarify Inspection empty state, add end-to-end smoke tests

## Notes

Three bugs, all of which failed silently while the app reported healthy, plus the test
coverage that would have caught them.

### 1. Docker destroyed all API state on every container recreation

`Program` wrote the SQLite database and data protection keys to the platform
application-data directory -- `/root/.config/shmoxy-api` on Linux. `scripts/start.sh`
mounted its volume at `/root/.local/share/shmoxy-api`, a different directory
(`LocalApplicationData`, not `ApplicationData`). Verified before the fix: the mounted
volume was empty (`total 0`) while the real state sat in the container's writable layer.

Consequences on every `docker run`: saved traces, sessions, settings and remote proxies
were recreated empty, and fresh data protection keys invalidated the antiforgery cookie the
browser still held -- which broke the Blazor circuit and made the UI look dead.

A local run was unaffected, because `ApplicationData` persists naturally outside a
container. That asymmetry is why it went unnoticed.

Fix: `ApiConfig.DataDirectory` makes the path explicit and configurable, the Dockerfile
pins it to `/data`, and `start.sh` mounts the volume there. No migration needed -- the old
mount point never held data.

### 2. `--proxy-port` silently ignored in bare-metal mode

`ProxyProcessManager` resolves the port as
`_portOverride ?? persistedConfig?.Port ?? ApiConfig.ProxyPort`, so a persisted port beats
`--proxy-port`. Docker hides this by mapping the requested host port onto the persisted one.
Bare metal has no mapping, so `start.sh --no-docker --proxy-port 8866` printed
"proxy on port 8866" while the proxy bound 18080 and 8866 was dead.

Fix: the bare-metal path now resolves and announces the effective port, and warns loudly
when a persisted config overrides the request. Precedence itself is unchanged -- a saved
port is a deliberate user setting, and silently outranking it would be its own surprise.
See follow-ups.

### 3. Inspection empty state was actively misleading

`Inspection.razor` always rendered "Start the proxy to see requests" whenever the table was
empty, with no reference to actual state. When the proxy *was* running this contradicted the
Proxy tab and sent debugging toward the proxy while the real fault was the event stream. It
cost real time during this investigation.

Fix: the message now reflects connection state and carries a code --
`INSP-DISCONNECTED`, `INSP-RECONNECTING`, `INSP-FILTERED`, `INSP-WAITING`.

### The missing test

Every layer had passing unit tests while the running app was useless. Nothing booted the
app the way `start.sh` does and asserted that traffic reaches a consumer.

`ProxyCaptureSmokeTests` now does exactly that: boots the real API host on a real port, lets
it spawn the real proxy binary as a child process, pushes real HTTP through that proxy, and
asserts the events arrive at the SSE endpoint. It also asserts the stream establishes while
the proxy is idle, and that state lands in the configured data directory.

Proof it works: reverting the #345 idle-stream fix makes it fail with a timeout, and
restoring the old volume mount path makes `StartScriptTests` fail with
`Expected: "/data"  Actual: "/root/.local/share/shmoxy-api"`.

### Verification

- Full suite: **895 passed, 0 failed, 0 warnings** (up from 875; +20 tests).
- Each new guard confirmed to fail when its bug is reintroduced.
- **Docker via `scripts/start.sh`**: proxy Running, idle stream headers in under 2s, HTTP
  and HTTPS both captured (2 request + 2 response events), UI serves 200, state present in
  the `/data` volume.
- **Docker persistence across recreation**: wrote a retention setting and recorded the key
  id in one container, destroyed it, started a fresh container on the same volume -- same
  key file and the setting intact.
- **Local via `scripts/start.sh --no-docker`**: boots, proxy Running, and the port warning
  fires correctly on a mismatch. (The EF `fail` lines during startup are the expected,
  caught `AddColumnIfMissing` probes.)

### Follow-ups (not in this PR)

- `InspectionHook.GetReader()` hands the same single-consumer `ChannelReader` to every
  subscriber, so two concurrent stream consumers split events instead of each receiving all
  of them. Wrong for anything beyond one browser tab.
- `HttpClient.Timeout` still applies to reads from a `ResponseHeadersRead` content stream, so
  a long-lived SSE connection may be torn down every 100s. It reconnects now rather than
  dying, but a dedicated streaming client with an infinite timeout would remove the churn.
- Consider an explicit `ApiConfig:ProxyPortOverride` so `--proxy-port` can win over a
  persisted config instead of only warning.

---
*This document was auto-generated by scripts/new-pr.sh*
