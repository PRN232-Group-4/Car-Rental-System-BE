# 2026-10-02 — Local migration-path resolution

- **FR/NFR:** NFR-001, NFR-008
- **Type:** fix

## Delivered

- Migration startup now resolves SQL from the published content root in deployment and from the repository root when launched through `dotnet run` in `src/VehicleRental.WebApi`.
