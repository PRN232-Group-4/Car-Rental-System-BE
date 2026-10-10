# FR-006 — Giấy phép lái xe khách
- **Status:** In progress (submission, Cloudinary upload and review implemented); **Priority:** P1; **Milestone:** M2
Nhập số/hạng/ngày/ảnh giấy phép, staff duyệt. Trạng thái pending/approved/rejected.

## 2026-10-10 — Upload và cập nhật giấy phép
- Customer gửi `PUT /api/customer/license` bằng `multipart/form-data`: `number`, `type`, `issueDate` nullable, `expiryDate` bắt buộc, `file` bắt buộc. JPEG/PNG/GIF/WebP, tối đa 5 MB; kiểm tra loại nội dung, kích thước và chữ ký ảnh trước khi upload.
- Application kiểm tra số/hạng, ngày cấp không sau ngày hết hạn và giấy phép không hết hạn trước khi gọi Cloudinary. Chỉ lưu secure URL sau upload thành công; mọi lần sửa đặt lại `pending`.
- Giữ cách lưu URL cũ qua `PUT /api/customer/license/url` bằng JSON. Không upload file/URL từ server ở endpoint này.
- GET license và review giữ contract hiện tại; chỉ Customer tự sửa, Admin/Manager/CustomerService duyệt.
- Acceptance: multipart xuất hiện trong Swagger với file binary; ảnh/field sai trả 400 không upload; lỗi cấu hình/provider upload trả 500 an toàn không lộ chi tiết provider và không sửa DB; thành công lưu URL HTTPS và pending, response đủ 6 trường.
- Private file access còn pending: adapter hiện trả secure HTTPS URL Cloudinary; chưa dùng authenticated delivery. Không xem HTTPS là private access.
