# Staff authorization and frontend contract

The authoritative capability matrix is [FR-007](../specs/FR-007-rbac.md). `Staff` only identifies an employee; it does not grant operational permissions. Department describes the organization and does not grant a role automatically. Permissions are additive if an account has multiple roles.

| Staff specialty | API value | UI label |
|---|---|---|
| Customer support | `CustomerService` | Chăm sóc khách hàng |
| Driver | `Driver` | Tài xế |
| Technician | `Maintenance` | Kỹ thuật / bảo trì |

## Create and update staff

Only Admin can call `POST /api/staff` or `PUT /api/staff/{id}`. Add `staffRole` to the existing staff payload:

```json
{
  "email": "support@example.test",
  "fullName": "Customer Support",
  "password": "replace-with-a-strong-password",
  "departmentId": 2,
  "facilityId": 1,
  "isDriver": false,
  "staffRole": "CustomerService"
}
```

Department/facility IDs must exist. Driver creation uses `isDriver: true`, `staffRole: "Driver"`, and valid license fields. A missing role with `isDriver: true` infers Driver for compatibility. `Maintenance` and `CustomerService` require `isDriver: false`. Arbitrary role names including Admin/Manager are rejected. Existing administrative roles remain managed outside the specialty field.

PUT replaces the specialty: send the current `staffRole` to retain it. Null for a non-driver removes the specialty and leaves the employee marker Staff. This does not remove separately assigned Admin/Manager roles. Staff responses include `staffRole` so edit forms can round-trip it.

## Department assignment (2026-10-05)

Call `PUT /api/departments/{departmentId}/members/{userId}` with no request body and the existing authentication cookies. For example, `PUT /api/departments/2/members/45` moves user **45** to department **2**. The path uses `userId`, not the staff profile's `id`; staff responses expose both.

| Target account | Admin caller | Manager caller |
|---|---|---|
| Staff without Admin/Manager roles | Allowed | Allowed |
| Manager (including Manager + Staff) | Allowed | 403 |
| Admin + Staff/Manager | Allowed | 403 |
| Customer or account without Staff/Manager role | 400 | 400 (403 if Admin) |

The target must be active and the department must exist. Missing/inactive users return 404; invalid department returns 400. This replaces the user's single department membership. It does not promote someone to Manager, change specialty, or modify other profile fields. Existing Staff/Manager accounts without a staff profile receive a minimal profile. Existing roles are preserved. Success returns the normal envelope with updated `Staff` data and message `Employee assigned to department successfully.` Old access/refresh credentials are invalidated; the target signs in again.

Admin and Manager can read `GET /api/departments`, `GET /api/staff`, `GET /api/staff/{id}`, and `GET /api/staff/drivers`. Personnel creation, full update, deactivation, and department CRUD remain Admin-only. Manager assignments operate across departments; membership does not impose a department scope. Administrative role provisioning remains outside this endpoint. No new migration is required; authorization-version triggers from migration 0008 must already be installed.

## Session and profile

Facility assignment uses `PUT /api/facilities/{facilityId}/members/{userId}` with no body and the same role matrix as department assignment. Example: `PUT /api/facilities/3/members/45`. The target must be an active existing Staff/Manager account; Admin assigns either, Manager cannot assign Manager/Admin targets. The facility must exist (400 otherwise); missing/inactive user returns 404. Only `facilityId` changes; department, roles, specialty, and other fields are preserved. Missing employee profiles are created minimally. Success returns updated Staff in the usual envelope with message `Employee assigned to facility successfully.` Credentials are invalidated and the target must sign in again. No migration is required. `GET /api/facilities` is already available for selecting a facility. Manager authority remains system-wide.

Frontend sends cookies with requests and reads `GET /api/account/profile`; it does not read HttpOnly cookies. The successful response's `data` includes:

```json
{
  "email": "support@example.test",
  "fullName": "Customer Support",
  "gender": null,
  "phone": null,
  "avatarUrl": null,
  "roles": ["Staff", "CustomerService"],
  "staffId": 45,
  "departmentId": 2,
  "departmentName": "Customer service",
  "facilityId": 1
}
```

JWT claims carry corresponding `staff_id`, `department_id`, `department_name`, `facility_id`, role claims, and `authorization_version`. Customer accounts have null staff fields in the profile and no staff claims. No department code is introduced: use department ID for identity and name for display. Do not infer permissions from department name.

Navigation may use roles, but the API always enforces policies and resource ownership. Driver schedule requests omit `driverStaffId` to receive only their own schedule; requesting another driver returns 403. A rental outside the caller's scope returns 404. Inspection requests can omit `staffId`; if supplied, it must match the current employee. Even Admin must have a staff profile to personally record an inspection.

Maintenance personnel currently work across the entire fleet. Facility/department membership and maintenance assignment are not authorization boundaries.

## Deployment and revocation

Apply migration `0008_staff_authorization` before serving the updated application. The normal migration runner applies it on startup when migrations are enabled. This seeds CustomerService and adds database authorization-version triggers; it never assigns CustomerService to existing Staff automatically.

Admin must explicitly assign the appropriate specialty to existing employees. Existing access tokens without the version claim are rejected; refresh may replace a legacy token only when its stored version matches the current account. On staff updates/deactivation the API revokes Redis sessions. Role membership, staff metadata, active flag and password changes also invalidate tokens through the database version, including changes performed outside the API. Refresh cannot revive an old permission snapshot. In-flight requests already authorized are not cancelled.

JWT validation reads the active account from PostgreSQL on each authenticated request in addition to the Redis session check. This deliberately avoids cached stale permissions. A permission change requires sign-in again when refresh returns 401.

## Verification and boundaries

Tests cover actual authorization policy evaluation, controller scope enforcement, JWT claims, refresh version rejection, specialty assignment, ownership, and migration triggers. PostgreSQL tests use disposable databases; refresh tests use an in-memory Redis command substitute. Browser cookie flows and live Redis are not end-to-end tested here. CSRF remains the separately tracked unfinished requirement in FR-003.

## Override 2026-10-04 — FR-031
Manager/Admin quản lý ca trực, assignment, replacement và giao maintenance. CustomerService không assign driver. Maintenance chỉ sửa công việc được giao, không đổi assignee; Manager/Admin tạo maintenance. Staff/Driver report với staff_id từ principal; Driver chỉ report chuyến được phân công.
