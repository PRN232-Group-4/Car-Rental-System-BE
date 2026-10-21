# FR-040 — Số việc chờ xử lý trên điều hướng Admin

- `GET /api/admin/navigation-counts` trả một snapshot gồm `rentals`, `inspections`, `maintenance`, `reports`, `licenses` (số nguyên không âm), nằm trong envelope API chung.
- “Mới” là việc đang chờ xử lý, không phải trạng thái đã đọc của từng người. Không lưu mốc đọc hoặc cộng các trang dữ liệu trên frontend.
- `rentals`: yêu cầu `pending`; `inspections`: đơn `pre_inspection`/`post_inspection` chưa có biên bản tương ứng cho xe hiện tại; `maintenance`: phiếu `open`/`scheduled`; `reports`: báo cáo nhân viên `open`; `licenses`: khách hàng có số GPLX thực, `pending`, và tài khoản tồn tại.
- Tổng theo cùng phạm vi của các API danh sách hiện có: các queue toàn hệ thống, riêng reports chỉ Admin/Manager xem tất cả; nhân viên khác chỉ đếm báo cáo của `staff_id` của chính mình. Không tự thêm phân vùng theo cơ sở khi API danh sách chưa giới hạn cơ sở.
- Admin/Manager nhận mọi tổng; CustomerService nhận rentals/inspections/licenses; Maintenance nhận inspections/maintenance; các role nhân viên có thêm reports theo phạm vi trên (thiếu staff_id hợp lệ trả 0). Staff chung chỉ có reports của chính mình; Customer/Driver/khách không đăng nhập bị từ chối. Vai trò kết hợp nhận hợp các quyền.
- Menu cha Vận hành và Chăm sóc & Sự cố có dot góc phải khi một mục con có số > 0; mục con hiển thị số tương ứng. Duyệt GPLX có đường dẫn riêng tới danh sách khách hàng chờ duyệt. Số liệu tự làm mới và được invalidation sau thao tác liên quan; lỗi tải không dựng số mới giả.

## Acceptance

- Kiểm tra HTTP/RBAC/envelope/Swagger thật; kiểm tra truy vấn với PostgreSQL cho trạng thái chờ/đã xử lý, GPLX trống và biên bản xe cũ.
- Không trả dữ liệu cá nhân trong snapshot; không thay đổi quyền hoặc nghiệp vụ xử lý.
