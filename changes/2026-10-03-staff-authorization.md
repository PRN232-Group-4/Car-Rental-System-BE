# 2026-10-03 — Staff specialties and resource authorization

- **FR/NFR:** FR-003, FR-007..009, FR-014..016, FR-018..024, FR-026, FR-030, NFR-004, NFR-007
- **Type:** fix, feat

## Delivered

- Replaced broad Staff access with named policies for customer service, management, inspection, maintenance, financial read and rental read. Driver/Staff alone cannot request refunds or modify inspections/catalog.
- Admin can assign CustomerService, Driver or Maintenance through staffRole; validation prevents Admin/Manager injection and conflicting driver flags. Existing Staff are not automatically elevated.
- Added employee, department and facility metadata to account profile and access JWT. Department is distinct from role; no department-name matching.
- Migration 0008 seeds CustomerService, adds authorization_version and triggers for role membership, staff changes, account activation/password changes. Access and refresh reject old versions; staff update/deactivate also revoke Redis sessions.
- Scoped rental details to customer ownership or current driver assignment, and driver schedules to the authenticated driver. Inspection actor comes from authenticated staff; conflicting body staffId is forbidden.
- Closed subscription ownership bypass and status-transition bypass of required inspections.
- Updated affected specs and added frontend/deployment instructions in docs/staff-authorization.md.

## Verification

- `dotnet test tests/VehicleRental.Tests/VehicleRental.Tests.csproj --no-restore`: 69 passed, 0 failed, 0 skipped against isolated PostgreSQL 18, applying baseline plus ordered migrations. All projects compiled successfully.
- Refresh version/rotation controller tests use an in-memory Redis protocol substitute; live Redis/browser cookie end-to-end verification is not included.
- Docker daemon was unavailable; PostgreSQL tests used local PostgreSQL binaries and a separate temporary cluster, not application data.

## Compatibility

Deploy migration 0008 with the application. Staff edit forms must round-trip staffRole; null clears a non-driver specialty. Assign specialties to legacy employees before expecting their former broad operational access. Existing JWTs without authorization_version require refresh/sign-in. Maintenance operates across the fleet; facility-level scoping and CSRF remain outside this change.
