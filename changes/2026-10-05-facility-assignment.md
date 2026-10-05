# 2026-10-05 — Facility assignment

- **FR/NFR:** FR-007, FR-008, NFR-004, NFR-007
- Added `PUT /api/facilities/{facilityId}/members/{userId}` without a request body. Admin assigns Manager/Staff; Manager assigns Staff only, including protection for multi-role targets.
- Reuses the department assignment transaction and authorization checks; only the chosen membership column changes. Roles, specialties, department and other profile fields remain intact. Invalid references and inactive/non-employee accounts are rejected. Existing credentials are invalidated and Redis sessions revoked.
- No migration required. As with departments, assignment does not grant Manager role; Manager scope is system-wide.
- Tests cover policy evaluation, role preservation, department/facility independence, authorization version invalidation, missing/inactive users, invalid facility, and protected targets. Live Redis/browser flow is not end-to-end tested.

Verification: `dotnet test tests/VehicleRental.Tests/VehicleRental.Tests.csproj --no-restore` passed 116/116 tests with isolated PostgreSQL 18 on localhost port 55439. All projects compiled; no application/Neon data was changed.
