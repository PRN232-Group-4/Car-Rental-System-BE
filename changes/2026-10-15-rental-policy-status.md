# Trạng thái chính sách thuê xe — 2026-10-15

Người quản lý cần bật/tạm ngưng phiên bản chính sách đã tạo. Bổ sung `PATCH /api/rental-policies/{id}/status` với `{ "isActive": true/false }`, quyền Admin/Manager. Validate tại HTTP và Application; ID không tồn tại 404. SQL UPDATE một cột với RETURNING, không đổi điều khoản, không tạo bản ghi mới, không đổi policy khác hay snapshot rental/invoice.

Cập nhật FR-011 và ADR-006 để cho phép ngoại lệ trạng thái bên cạnh thiết kế điều khoản bất biến. Swagger mô tả body, quyền, success/error envelope; message thành công cụ thể. API danh sách không thay đổi.

Kiểm chứng: `dotnet test ... -warnaserror` build toàn bộ project/test không warning/error; nhóm RentalPolicyStatusApiTests, UnitTests, AuthorizationTests, ApiResponseTests có 126/126 test đạt, gồm 19 test HTTP mới. Bao phủ false, thiếu/null/sai kiểu, ID sai/không có, quyền, idempotency, giữ điều khoản, envelope JSON và Swagger. Lần lọc test đầu tiên chọn thêm 9 StaffAuthorizationTests dùng DB; chúng thất bại do mật khẩu PostgreSQL test mặc định không hợp lệ. Không sửa những test đó.

Thêm RentalPolicyStatusPersistenceTests cho cập nhật thật, ưu tiên policy active, không đổi điều khoản/bản ghi khác/số lượng và snapshot rental/invoice. Chưa xác nhận test này với PostgreSQL hợp lệ; Docker daemon local chưa chạy và chưa có TEST_DATABASE_ADMIN. Không chạy migration hoặc ghi dữ liệu production. Cần deploy backend cùng frontend để dùng endpoint mới trên môi trường triển khai.
