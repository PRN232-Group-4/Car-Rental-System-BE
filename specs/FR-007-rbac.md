# FR-007 — RBAC
- **Status:** In progress; **Priority:** P0; **Milestone:** M2

Roles: Customer, Staff (employee marker only), CustomerService, Driver, Maintenance (Technician in UI), Manager, Admin. Department is organizational metadata, not an implicit permission grant. Only Admin assigns staff specialties. Existing Staff accounts require explicit specialty assignment; no automatic CustomerService backfill.

| Capability | Roles |
|---|---|
| Organization/staff CRUD, refund approval, destructive catalog actions | Admin |
| Vehicle catalog, subscription plans, rental policies, dashboard | Admin, Manager |
| License review, rental lifecycle/assignment, invoices, payment/refund requests | Admin, Manager, CustomerService |
| Inspections | Admin or Maintenance, with own active staff profile |
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
