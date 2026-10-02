# 2026-10-02 — Cross-platform migration checksum

- **FR/NFR:** NFR-001, NFR-008
- **Type:** fix

## Delivered

- Normalized migration checksums to LF line endings so Linux deploys and Windows local runs calculate the same hash.
- Accepted the equivalent legacy CRLF checksum already stored by earlier Windows runs; altered SQL content remains rejected.
