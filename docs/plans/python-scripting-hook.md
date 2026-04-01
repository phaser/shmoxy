# Python Scripting Hook

Add a Python scripting system to the proxy so user-defined scripts can intercept and transform requests. The first script is a "CCH Signer" that adds Anthropic client attestation headers. An integration test validates the full pipeline: enable script via API, make a Claude API call through the proxy, verify the script modifies the request and the call succeeds.

GitHub issue: #209

## Open Gaps (decisions needed)

### 1. Process-based vs pythonnet

**Recommendation: Process-based (subprocess).**
- No NuGet dependency or CPython linking
- Works with any Python version on PATH
- Scripts run only on pattern-matched requests (low frequency), so ~50ms subprocess overhead is negligible
- Cleaner error isolation (crashed script = crashed subprocess, not crashed proxy)

Alternative: pythonnet embeds CPython for fastest execution and direct .NET interop, but requires CPython installed and version-matched, adds deployment/Docker complexity.

### 2. Script metadata format

Scripts declare their own filter via well-known Python variables, parsed with regex (no execution needed):
```python
SCRIPT_NAME = "CCH Signer"
SCRIPT_DESCRIPTION = "Adds CCH hash to Anthropic API requests"
FILTER_PATTERN = "api.anthropic.com"
FILTER_METHOD = "POST"  # optional
```

### 3. Script storage

- **Default scripts**: `src/shmoxy/scripts/` copied to build output via csproj `<Content>`
- **User scripts**: `{CertStoragePath}/scripts/` (e.g. `~/Library/Application Support/shmoxy/scripts/`)
- **State file**: `scripts.json` in user scripts dir tracks enabled/disabled per script

### 4. Script dependencies

The CCH script needs the `xxhash` Python package. No auto-install -- script outputs a clear error if missing. Test skips gracefully if not installed.

### 5. Test preconditions

Test needs: Python 3, xxhash, Claude API key. Uses `[SkippableFact]` and skips with clear message if any is missing. API key sourced from `ANTHROPIC_API_KEY` env var or macOS Keychain.

### 6. Hook ordering

`InspectionHook -> ScriptingHook -> BreakpointHook`. Inspection captures raw request, scripts modify it, breakpoints can pause the modified version.

### 7. Multiple matching scripts

Execute sequentially, each receives the output of the previous. Ordered: built-in first, then user scripts alphabetically.

### 8. Response scripting

v1 is request-only. `OnResponseAsync` passes through unchanged.

## Implementation

### Subprocess contract

Script receives JSON on stdin:
```json
{
  "method": "POST",
  "url": "https://api.anthropic.com/v1/messages",
  "host": "api.anthropic.com",
  "path": "/v1/messages",
  "headers": {"content-type": "application/json"},
  "body": "...raw body as UTF-8 string..."
}
```

Script writes JSON to stdout (only modified fields):
```json
{
  "headers": {"x-anthropic-billing-header": "cch=XXXXX;cv=1"},
  "body": "...modified body if needed..."
}
```

Missing fields = no change. Scripts can modify headers and body but not method/url (safety constraint). On any script error (crash, timeout, bad output), log and pass through original request unchanged.

### New files (4)

| File | Purpose |
|------|---------|
| `src/shmoxy/models/dto/ProxyScript.cs` | Script metadata model (Id, Name, Description, FilterPattern, FilterMethod, FilePath, Enabled, IsBuiltIn, LoadedAt) |
| `src/shmoxy/server/hooks/ScriptingHook.cs` | Core hook: loads scripts, matches requests, runs via subprocess, CRUD methods, state persistence |
| `src/shmoxy/scripts/cch_signer.py` | Default CCH signing script (xxHash64 with seed `0x6E52736AC806831E`, masked to 20 bits) |
| `src/tests/shmoxy.e2e/ScriptingHookTests.cs` | Integration tests: CRUD lifecycle + end-to-end CCH signing through proxy |

### Modified files (5)

| File | Change |
|------|--------|
| `src/shmoxy/ipc/ProxyStateService.cs` | Add `ScriptingHook?` constructor param + property |
| `src/shmoxy/ipc/ProxyControlApi.cs` | Add 6 script CRUD endpoints (list, get, add, enable, disable, delete) |
| `src/shmoxy/ShmoxyHost.cs` | Register ScriptingHook in DI, update hook chain, update ProxyStateService factory |
| `src/shmoxy/shmoxy.csproj` | Include `scripts/**` as content files copied to output |
| `src/tests/shmoxy.e2e/shmoxy.e2e.csproj` | Add `Xunit.SkippableFact` package |

### API endpoints

```
GET    /ipc/scripts              -- list all scripts with metadata
GET    /ipc/scripts/{id}         -- get single script
POST   /ipc/scripts              -- add script (body: {fileName, content})
POST   /ipc/scripts/{id}/enable  -- enable a script
POST   /ipc/scripts/{id}/disable -- disable a script
DELETE /ipc/scripts/{id}         -- remove user script (rejects built-in)
```

### Integration test flow

1. Start ProxyServer with ScriptingHook + InspectionHook
2. Create IPC host
3. List scripts via `GET /ipc/scripts` -- CCH Signer should be present (disabled)
4. Enable CCH Signer via `POST /ipc/scripts/{id}/enable`
5. Extract Claude API key from `ANTHROPIC_API_KEY` env var or macOS Keychain
6. Skip if no key / no Python / no xxhash
7. Make POST to `https://api.anthropic.com/v1/messages` through proxy (without CCH)
8. Script intercepts, computes xxHash64 of body, adds `x-anthropic-billing-header`
9. Assert: response is not a server error (proves script ran without breaking the request)

### CCH algorithm

```
hash = xxHash64(body_utf8_bytes, seed=0x6E52736AC806831E)
cch  = format(hash & 0xFFFFF, "05x")   # lower 20 bits, 5-char hex
header: x-anthropic-billing-header: cch={cch};cv=1
```

### Implementation order

1. ProxyScript.cs + cch_signer.py + shmoxy.csproj (parallel, no deps)
2. ScriptingHook.cs (depends on ProxyScript)
3. ProxyStateService.cs + ShmoxyHost.cs + ProxyControlApi.cs (depend on ScriptingHook)
4. shmoxy.e2e.csproj + ScriptingHookTests.cs (depend on everything)

## Verification

1. `dotnet build` -- zero warnings
2. `dotnet test` -- all existing tests pass + new tests pass
3. Manual: start proxy, `curl /ipc/scripts` shows CCH Signer, enable it, make a request through proxy to api.anthropic.com, verify `x-anthropic-billing-header` is added
