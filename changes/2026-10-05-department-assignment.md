# 2026-10-05 — Department assignment

- **FR/NFR:** FR-007, FR-008, NFR-004, NFR-007
- Added `PUT /api/departments/{departmentId}/members/{userId}`. Admin assigns Staff/Manager; Manager assigns Staff only. Protected roles take precedence on multi-role targets.
- Validates active employee and department, preserves roles/specialties and unrelated profile fields, creates a minimal employee profile when necessary, and invalidates existing credentials.
- Manager can read staff and departments. Full personnel mutations and department CRUD remain Admin-only.
- No schema migration; existing migration 0008 provides authorization-version invalidation. Redis sessions are revoked after assignment.
- Assumptions: assigning means moving an existing employee's department, not granting Manager role; Manager scope remains system-wide. A department may contain multiple Managers, as the current schema imposes no unique department-manager relationship.

## Verification

`dotnet test tests/VehicleRental.Tests/VehicleRental.Tests.csproj --no-restore`: **112 passed, 0 failed, 0 skipped**, with a temporary PostgreSQL 18 cluster on localhost port 55439 and disposable test databases. All projects compiled. Tests cover endpoint authorization, protected multi-role targets, invalid/inactive targets, department validation, preservation of specialties/roles and authorization-version changes. The first attempt against the default local test server failed authentication; the isolated cluster run passed. No application/Neon database was modified. Browser and live Redis assignment flow were not end-to-end tested.
