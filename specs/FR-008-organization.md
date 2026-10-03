# FR-008 — Phòng ban, cơ sở, nhân viên
- **Status:** In progress; **Priority:** P1; **Milestone:** M3
CRUD department/facility/staff; driver là staff có `is_driver`. Acceptance: RBAC Admin, logical references validated.

## Phân quyền cập nhật 2026-10-03

Admin quản lý nhân sự. Payload create/update bổ sung `staffRole`: `CustomerService`, `Driver`, `Maintenance` hoặc null. `isDriver=true` yêu cầu role Driver (bỏ trống được suy ra Driver); null với non-driver nghĩa là Staff chưa có quyền nghiệp vụ. PUT thay thế specialty, vì vậy phải gửi lại staffRole muốn giữ. Response staff trả staffRole. Không nhận Admin/Manager qua trường này. Update/deactivate vô hiệu token cũ; xem FR-007.
