# FR-008 — Phòng ban, cơ sở, nhân viên

## Department assignment (2026-10-05)

Facility assignment follows the same rules via `PUT /api/facilities/{facilityId}/members/{userId}` (no body). Only facility membership changes; department, roles and other staff fields are preserved. Invalid facility returns 400. Both assignment endpoints invalidate old credentials and accept existing active Staff/Manager accounts only.

- `PUT /api/departments/{departmentId}/members/{userId}` assigns an existing active Staff or Manager account to an existing department.
- Admin may assign Staff or Manager; Manager may assign Staff only. A target with Manager or Admin role is protected even when it also has Staff role.
- This changes department membership only, never account roles or staff specialties. An existing Manager without a staff profile gets a minimal profile to store membership.
- Manager access is system-wide, consistent with current management policies. Both roles may read departments and staff to select members; personnel CRUD remains Admin-only.
- Invalid IDs/reference or non-employee targets return 400, missing/inactive users return 404, protected targets for Manager return 403. Membership changes invalidate old credentials.
- **Status:** In progress; **Priority:** P1; **Milestone:** M3
CRUD department/facility/staff; driver là staff có `is_driver`. Acceptance: RBAC Admin, logical references validated.

## Phân quyền cập nhật 2026-10-03

Admin quản lý nhân sự. Payload create/update bổ sung `staffRole`: `CustomerService`, `Driver`, `Maintenance` hoặc null. `isDriver=true` yêu cầu role Driver (bỏ trống được suy ra Driver); null với non-driver nghĩa là Staff chưa có quyền nghiệp vụ. PUT thay thế specialty, vì vậy phải gửi lại staffRole muốn giữ. Response staff trả staffRole. Không nhận Admin/Manager qua trường này. Update/deactivate vô hiệu token cũ; xem FR-007.
