# FR-008 — Phòng ban, cơ sở, nhân viên

## Hồ sơ nhân viên từ tài khoản có sẵn — 2026-10-21

Admin/Manager chọn email tài khoản đang hoạt động từ user directory; POST `/api/staff/from-user` nhận userId cùng metadata nhân viên (phòng ban/cơ sở/ngày nhận việc và GPLX khi Driver). Email, họ tên, mật khẩu và vai trò được giữ từ tài khoản đã chọn, không tạo user mới. Server lấy Driver từ role lưu thật, không nhận isDriver/staffRole để đổi quyền qua biểu mẫu này; thêm marker Staff và giữ các vai trò đã có. Tài khoản đã có staff profile trả 409, tài khoản không tồn tại/đã khóa trả 404. Validation và tạo profile nằm trong transaction có account lock để chống hai lần tạo và đổi role đồng thời. GPLX bắt buộc với Driver; non-driver không lưu metadata GPLX. Existing POST `/api/staff` vẫn giữ tương thích cho client cũ.

`Staff` response bổ sung `roles` đầy đủ, `isDriver` phản ánh role Driver của tài khoản. Account/profile response có `facilityName` authoritative lấy từ cơ sở liên kết hồ sơ; null khi chưa phân công, đọc được cho mọi tài khoản authenticated qua `/api/account/profile` mà không cần quyền quản trị directory.

Acceptance: [x] chọn existing user, giữ mật khẩu/identity/roles; [x] GPLX theo role Driver; [x] chống tạo trùng đồng thời và logical references; [x] cơ sở profile đúng mọi role; [x] email/tên cơ sở trên phiếu bảo trì; [x] HTTP quyền/validation/envelope/Swagger và PostgreSQL thật. Xem StaffIdentityTests.

## Department assignment (2026-10-05)

Facility assignment follows the same rules via `PUT /api/facilities/{facilityId}/members/{userId}` (no body). Only facility membership changes; department, roles and other staff fields are preserved. Invalid facility returns 400. Both assignment endpoints invalidate old credentials and accept existing active Staff/Manager accounts only.

- `PUT /api/departments/{departmentId}/members/{userId}` assigns an existing active Staff or Manager account to an existing department.
- Admin may assign Staff or Manager; Manager may assign Staff only. A target with Manager or Admin role is protected even when it also has Staff role.
- This changes department membership only, never account roles or staff specialties. An existing Manager without a staff profile gets a minimal profile to store membership.
- Manager access is system-wide, consistent with current management policies. Both roles may read departments and staff to select members; personnel CRUD permits Admin/Manager under the 2026-10-21 authorization update.
- Invalid IDs/reference or non-employee targets return 400, missing/inactive users return 404, protected targets for Manager return 403. Membership changes invalidate old credentials.
- **Status:** In progress; **Priority:** P1; **Milestone:** M3
CRUD department/facility/staff; driver là staff có `is_driver`. Acceptance: RBAC Admin, logical references validated.

## Phân quyền cập nhật 2026-10-03

Admin/Manager quản lý nhân sự (cập nhật quyền 2026-10-21). Payload create/update bổ sung `staffRole`: `CustomerService`, `Driver`, `Maintenance` hoặc null. `isDriver=true` yêu cầu role Driver (bỏ trống được suy ra Driver); null với non-driver nghĩa là Staff chưa có quyền nghiệp vụ. PUT thay thế specialty, vì vậy phải gửi lại staffRole muốn giữ. Response staff trả staffRole. Không nhận Admin/Manager qua trường này. Update/deactivate vô hiệu token cũ; xem FR-007.

## 2026-10-10 — Upload ảnh giấy phép tài xế
Admin/Manager dùng `POST /api/media/staff-license` (`multipart/form-data`, `file` bắt buộc), JPEG/PNG/GIF/WebP tối đa 5 MB. Response `StoredImage` có `secureUrl` và `publicId`; truyền URL vào `licenseImageUrl` của create/update staff hiện có. Upload không tự sửa nhân viên hoặc đổi authorization version. Theo quyết định 2026-10-21, policy Management cho phép Admin/Manager upload khi tạo/sửa hồ sơ; Customer/Staff/Driver/Maintenance/CustomerService không được phép.
