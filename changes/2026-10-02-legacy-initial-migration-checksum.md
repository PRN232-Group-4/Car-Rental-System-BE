# 2026-10-02 — Legacy initial-migration checksum compatibility

- **FR/NFR:** NFR-001, NFR-008
- **Type:** fix

## Delivered

- Preserved checksum validation for immutable post-baseline migrations.
- Allowed legacy databases with an older recorded `0001_initial_schema` checksum to start and apply migrations `0002+`, because the repository baseline was historically changed and those changes have dedicated follow-up migrations.
