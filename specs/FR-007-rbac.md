# FR-007 — RBAC
- **Status:** In progress; **Priority:** P0; **Milestone:** M2

Roles: Customer, Staff (employee marker only), CustomerService, Driver, Maintenance (Technician in UI), Manager, Admin. Department is organizational metadata, not an implicit permission grant. Admin/Manager may manage operational staff specialties through staff CRUD; global account-role membership remains Admin-only. Existing Staff accounts require explicit specialty assignment; no automatic CustomerService backfill.

The Admin-only [role catalog](FR-041-role-catalog.md) supports custom persisted roles and account membership. Custom names do not implicitly grant built-in policy capabilities. Built-in role names are immutable and cannot be deleted; assigned custom roles must be unassigned before rename/deletion. Admin and Manager may read the [account-administration audit directory](FR-037-user-administration.md).

Staff creation passwords follow the same 6–20 character limit as registration/login; invalid lengths return 400 validation. See [password-length change](../changes/2026-10-07-password-length.md).

| Capability | Roles |
|---|---|
| Department CRUD and refund approval | Admin |
| Staff/facility CRUD, staff-license upload, destructive fleet/plan catalog actions | Admin, Manager |
| Read staff/departments; assign Staff to a department | Admin, Manager |
| Assign Manager to a department (including Manager + Staff targets) | Admin |
| Assign Staff to a facility | Admin, Manager |
| Assign Manager to a facility (including Manager + Staff targets) | Admin |
| Vehicle catalog, subscription plans, rental policies, dashboard | Admin, Manager |
| License review, rental lifecycle/assignment, invoices, payment/refund requests | Admin, Manager, CustomerService |
| Customer directory list/detail | Admin, Manager, CustomerService |
| Inspections | Admin, Manager or Maintenance, with own active staff profile |
| Maintenance management | Admin, Manager, Maintenance |
| Driver schedule | Admin/Manager all; Driver own assignments |
| Rental detail | Admin/Manager/CustomerService/Maintenance; Customer own; Driver assigned |
| Customer license, booking, purchase, checkout | Customer |

Maintenance personnel operate across the fleet; department/facility claims do not impose a facility boundary. Assignment indicates maintenance responsibility, not an access boundary. Rental detail outside caller scope returns 404. Optional inspection staffId must match the authenticated staff; no impersonation even for Admin.

JWT/profile expose staffId, departmentId, departmentName, facilityId and roles. JWT and refresh records include authorization version. Database triggers increment it on role membership, staff metadata, activation or password changes. Authentication loads the active account and rejects stale versions. Staff update/deactivation additionally revoke Redis sessions.

## Acceptance
- [x] Policy tests deny Driver/Staff financial and inspection access.
- [x] Rental ownership and driver assignment scope covered by repository tests.
- [x] JWT claims and stale-version rejection covered by tests.
- [x] Specialty whitelist blocks Admin/Manager injection through staff payload.
