# Proxy streaming hypotheses

## Inspection retention ceiling

The bounded inspection channel can retain up to its event capacity multiplied by
the configured preview limit. Payload length no longer changes that ceiling, but
high capture limits combined with a slow or absent consumer may still create an
undesirably large fixed ceiling.

Status: unconfirmed. Measure retained heap under a saturated inspection channel
before changing channel capacity or introducing a byte-budgeted queue.
