# FR-009 — Loại xe và xe
- **Status:** In progress; **Priority:** P1; **Milestone:** M3
CRUD vehicle type, vehicle details/images. Cloudinary storage service và `POST /api/media/vehicle` đã có; gửi `secureUrl` trả về vào `mainImageUrl` khi tạo/sửa xe. Acceptance: subtype đúng vehicle type, status transition hợp lệ, pagination.

## Phân quyền cập nhật 2026-10-03

Phân quyền: đọc catalog public; tạo/sửa loại xe, xe và upload ảnh xe cho Admin/Manager; xóa loại xe chỉ Admin. Staff/Driver không tự có quyền quản lý catalog.

## 2026-10-04 — Biển số
Vehicle có licensePlate nullable, tối đa 255 ký tự trong create/update/list/detail. Migration 0011 dùng ADD COLUMN IF NOT EXISTS.
## Người bảo trì hiện tại — 2026-10-21

Vehicle list/detail bổ sung maintenanceTechnicianEmails là array email phân công hiện tại đã dedup/sắp xếp, chỉ trả cho Admin/Manager/Maintenance. Public/customer đọc array rỗng. Chỉ lấy nhân viên Maintenance đang active từ các phiếu không cancelled và còn open/scheduled/in_progress hoặc completed nhưng chưa an toàn (is_safe=false); không dùng completed-safe/cancelled làm người phụ trách hiện tại. Không có phân công phù hợp trả array rỗng.

- [x] SQL/HTTP regression xác minh dedup, current/unsafe work và loại inactive/wrong-role/historical assignees.
- [x] Public/customer/Driver/CustomerService không nhận email, Admin/Manager/Maintenance đọc đúng list/detail.

## 2026-10-23 — Hạng GPLX được chấp nhận
- Vehicle create/update/list/detail/search bổ sung allowedLicenseTypes, chuẩn hóa trim/uppercase/dedup, tối đa 30 hạng chữ/số dài 1–20 ký tự.
- So khớp chính xác hạng được cấu hình, không tự suy diễn thứ bậc pháp lý. Mảng rỗng giữ tương thích xe cũ: bất kỳ GPLX approved còn hạn.
- Acceptance: lưu/đọc cấu hình qua list/detail/search; hạng không khớp hoặc bằng hết hạn/chưa duyệt không thể đặt tự lái.

### 2026-10-23 — Điều kiện xuất hiện trong danh mục khách hàng

Xe chưa cấu hình hạng GPLX (`allowedLicenseTypes` rỗng hoặc không có hạng khác khoảng trắng) bị ẩn khỏi public search trước phân trang/đếm tổng. Quy tắc hiển thị này thay thế cách hiển thị tương thích cũ; xe vẫn còn trong quản trị và lịch sử. Quy tắc xác minh GPLX cho đơn hiện hữu giữ nguyên.
