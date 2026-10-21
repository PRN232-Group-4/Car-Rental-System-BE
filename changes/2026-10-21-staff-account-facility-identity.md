# Hồ sơ nhân viên từ tài khoản có sẵn và email phân công

- POST /api/staff/from-user Admin-only liên kết hồ sơ công tác với tài khoản active. Body gồm userId, departmentId/facilityId/hiredDate và GPLX. Driver lấy từ role lưu thật, giữ tài khoản/roles gốc, thêm Staff marker và thu hồi session; không nhận mật khẩu hoặc client tự đặt specialty.
- Logical reference, tài khoản trùng/khóa và GPLX Driver được kiểm tra. Transaction/account lock bảo vệ tạo đồng thời. POST staff cũ giữ tương thích.
- Staff có roles đầy đủ; isDriver và tìm tài xế dựa trên role. Search nhân sự nhận phone/license.
- Account/profile có facilityName authoritative nullable cho mọi role đọc cơ sở của chính mình mà không cần directory Admin.
- InspectionAssignment có technicianEmail; Maintenance list/save có assignedStaffEmail/facilityName.

Kiểm chứng: build solution -warnaserror đạt, 0 warning/error. **76/76 test đạt**, không skip: StaffIdentityTests, InspectionAssignmentTests, StaffAuthorizationTests, FacilityAssignmentTests, ApiResponseTests trên PostgreSQL18 test database timezone UTC. Bao phủ HTTP/envelope/Swagger/quyền, tạo trùng đồng thời, Driver và cơ sở từng role. Chưa chạy Docker hoặc deploy. Không thêm migration ngoài 0025 của phần phân công kiểm tra trước đó.
