# Proxy streaming knowledge

## Confirmed facts

- The `shmoxy` engine has no project reference to `shmoxy.frontend`.
- A standalone engine process uses `NoOpInterceptHook`; IPC/admin startup adds
  inspection and breakpoint hooks as optional adapters.
- Request and response forwarding supports content-length, chunked, and
  close-delimited framing with fixed-size transfer buffers.
- Chunked bytes are forwarded with their original framing and trailers, while
  inspection byte counts exclude chunk metadata.
- `InspectionCaptureLimitBytes` caps retained previews. Events carry the preview,
  total payload bytes, a truncation flag, and original content encoding.
- Response decompression is capped at the same limit, including highly
  compressible payloads.
- Request body replacement is supported only when the complete body fits the
  configured capture limit. A truncated preview is read-only.

## Verification

- Generated 32 MiB helper-stream coverage asserts an 8 KiB maximum read request
  and a 64 KiB retained preview.
- Local proxy integration coverage streams large content-length and chunked
  requests, plus content-length, chunked, and close-delimited responses.
