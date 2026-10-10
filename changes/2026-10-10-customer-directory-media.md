# 2026-10-10 — Customer directory và Cloudinary evidence uploads

- **FR/NFR:** FR-006, FR-007, FR-008, FR-009, FR-015, FR-016, FR-027, FR-032, NFR-001, NFR-004, NFR-008
- **Loại:** feat, fix, tests, docs

## Đã làm

- Thêm GET `/api/customers` với tìm tên/email/điện thoại, lọc `isActive`, phân trang 1–100 items và thứ tự customer ID ổn định. Offset dùng long để tránh tràn số ở page lớn.
- Thêm GET `/api/customers/{customerId}` trả profile, license và loyalty; phân biệt customer ID/user ID, hỗ trợ inactive và trả 400/404 đúng ngữ cảnh. Policy CustomerService áp dụng cho Admin/Manager/CustomerService.
- Tách use case và query qua CustomerService/ICustomerRepository/NpgsqlCustomerRepository; DTO không chứa credential/session/token.
- PUT `/api/customer/license` nhận multipart, kiểm tra số/hạng/ngày, MIME/kích thước/chữ ký ảnh trước upload, lưu Cloudinary secureUrl rồi đặt lại pending. Thiếu customer hoặc lỗi upload không ghi license.
- JSON lưu URL chuyển sang PUT `/api/customer/license/url`; client cũ cần đổi route hoặc gửi FormData.
- Thêm POST `/api/media/staff-license` (Admin), `/api/media/inspection` (Inspection), `/api/media/report` (vai trò tạo report với staff_id). Trả secureUrl để dùng trong JSON lưu nghiệp vụ.
- Avatar/ảnh xe đã có upload Cloudinary. Chứng từ hợp đồng ký dùng URL theo FR-013.
- Lỗi Cloudinary dùng ImageStorageException, trả 500 an toàn và không lộ thông báo SDK. Response/multipart schema Swagger và success message dùng contract chung 6 trường.

## Validation

- Test Application, HTTP thực tế, Swagger và truy vấn PostgreSQL: pagination/search/role/400/401/403/404, customer ID mapping, nullable profile, multipart file, reset pending, dữ liệu ảnh sai không upload và provider failure không lưu.
- Kiểm chứng toàn bộ build/test được ghi tại [progress](../docs/progress.md).
- Cloudinary dùng stub trong test; chưa gửi file thật hoặc gọi tài khoản triển khai.

## Giới hạn

- Không migration. Cloudinary private delivery và cleanup asset cũ vẫn pending; HTTPS URL hiện có không phải private URL.
- Docker image chưa kiểm chứng trong lượt này.
