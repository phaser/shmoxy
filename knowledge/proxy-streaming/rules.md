# Proxy streaming rules

- Forward payloads before considering any complete-body retention strategy.
- Bound every inspection representation, including decompressed output.
- Preserve HTTP framing on the wire; count payload bytes separately from chunk
  metadata.
- Treat truncated previews as read-only. Never silently replace the beginning of
  a large body and discard its remainder.
- Keep UI/API dependencies outside the proxy engine. Optional consumers attach
  through hook and IPC boundaries.
- Add generated-stream coverage for any forwarding change so retained memory is
  demonstrably independent of payload length.
