# FR-006 — Giấy phép lái xe khách
- **Status:** In progress (submission, Cloudinary upload and review implemented); **Priority:** P1; **Milestone:** M2
Nhập số/hạng/ngày/ảnh giấy phép, staff duyệt. Trạng thái pending/approved/rejected.

## 2026-10-10 — Upload và cập nhật giấy phép
- Customer gửi `PUT /api/customer/license` bằng `multipart/form-data`: tất cả `number`, `type`, `issueDate`, `expiryDate`, `file` đều bắt buộc. JPEG/PNG/GIF/WebP, tối đa 5 MB; kiểm tra loại nội dung, kích thước và chữ ký ảnh trước khi upload.
- Application kiểm tra số/hạng, ngày cấp không sau ngày hết hạn và giấy phép không hết hạn trước khi gọi Cloudinary. Chỉ lưu secure URL sau upload thành công; mọi lần sửa đặt lại `pending`.
- Giữ cách lưu URL cũ qua `PUT /api/customer/license/url` bằng JSON. Không upload file/URL từ server ở endpoint này.
- GET license và review giữ contract hiện tại; chỉ Customer tự sửa, Admin/Manager/CustomerService duyệt.
- Acceptance: multipart xuất hiện trong Swagger với file binary; ảnh/field sai trả 400 không upload; lỗi cấu hình/provider upload trả 500 an toàn không lộ chi tiết provider và không sửa DB; thành công lưu URL HTTPS và pending, response đủ 6 trường.
- Private file access còn pending: adapter hiện trả secure HTTPS URL Cloudinary; chưa dùng authenticated delivery. Không xem HTTPS là private access.

## 2026-10-22 — Phân biệt chưa gửi với chờ duyệt
- Tài khoản mới có bản ghi customer nhưng chưa có số GPLX: GET `/api/customer/license` trả bản ghi với `status=none`, dù dữ liệu cũ trong DB mặc định `pending`. Không đổi dữ liệu đã lưu khi đọc.
- Số GPLX null/rỗng/chỉ khoảng trắng đều là chưa gửi; hồ sơ có số GPLX giữ nguyên `pending`/`approved`/`rejected`. Không có customer thì vẫn trả null.
- Acceptance: customer mới chưa hiển thị đã gửi/chờ duyệt và vẫn gửi được ảnh GPLX lần đầu; sau gửi thành công mới chuyển pending. Bao phủ response HTTP thật, envelope, Swagger và form ở cả `/profile` và `/driver-license`.

## 2026-10-21 — Bắt buộc đầy đủ thông tin GPLX
- Cả hai cách gửi yêu cầu đủ số GPLX, hạng, ngày cấp, ngày hết hạn và ảnh. JSON `/url` bắt buộc `imageUrl` hợp lệ; multipart bắt buộc file ảnh mới mỗi lần gửi.
- HTTP và Application đều từ chối thiếu ngày cấp hoặc ảnh trước khi lưu; kiểm tra metadata trước upload, kiểm tra URL sau upload.
- Acceptance: bỏ từng trường bắt buộc trả 400, không upload/ghi dữ liệu; form hiển thị tất cả lỗi dưới trường tương ứng và focus lỗi đầu tiên. Hồ sơ đầy đủ vẫn được gửi duyệt với trạng thái `pending`.

## 2026-10-23 — Nhiều GPLX độc lập
- Customer thêm nhiều GPLX; cập nhật theo licenseId hoặc upsert số GPLX khi không có ID. Mỗi GPLX giữ hạng, ngày, ảnh và trạng thái duyệt riêng; sửa một bằng không hủy duyệt bằng khác.
- GET hồ sơ GPLX giữ trường cũ và bổ sung licenses; duyệt nhận licenseId tùy chọn. Dữ liệu cũ được chuyển sang bảng customer_licenses, giữ nguyên trạng thái.
- Tự lái yêu cầu ít nhất một GPLX approved đúng hạng xe, còn hạn đến hết kỳ thuê; có tài xế không yêu cầu GPLX khách.
- Acceptance: lưu/đọc nhiều bằng, sửa số bằng theo ID không tạo bản sao, không sửa bằng của khách khác, duyệt riêng; booking/edit/assignment/handover cùng kiểm tra hạng và hạn.
