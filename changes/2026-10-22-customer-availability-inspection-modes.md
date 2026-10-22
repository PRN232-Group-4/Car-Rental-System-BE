# Xe chưa được thuê và kiểm tra theo hình thức lái

Theo yêu cầu mới của người dùng, thay thế luồng chauffeur-only trước đó:

- Public search chỉ lấy xe available và không có rental đang renting/post_inspection, trước đếm tổng/phân trang. Xe đang thuê không xuất hiện dù chọn thời gian tương lai; đơn chờ vẫn khóa theo khoảng đặt. API tạo đơn kiểm tra lại dưới vehicle lock; Admin vẫn đọc được toàn bộ đội xe.
- Cả hourly/longterm lưu và trả đúng hasDriver. Đơn có tài xế tự có kết quả trước thuê đạt, nguồn driver_default/system, không gán tài xế hoặc tạo số đo/ảnh. Đổi xe có xác nhận riêng cho xe hiện tại; không ghi đè kiểm tra fail/pass thật. Sửa trước bàn giao có thể chuyển tự lái và bỏ xác nhận mặc định; evidence kỹ thuật/Admin vẫn khóa sửa điều khoản.
- Admin/Manager phân công Maintenance và chờ kiểm tra như cũ. Admin có thêm POST `/api/rentals/{id}/inspection-assignment/checked`, body technicianStaffId: phân công + đạt kiểm tra trong một transaction. Người xác nhận lấy từ principal, nguồn admin_confirmation; chỉ trước bắt đầu và khi chưa có evidence. Không thay các điều kiện bắt đầu/hoàn tất hoặc phí trả trễ.
- Directory kiểm tra trả confirmationSource/confirmedBy để phân biệt xác nhận hệ thống/Admin với kiểm tra kỹ thuật. Migration 0028 backfill đơn có tài xế chưa bắt đầu, giữ hợp đồng dài hạn cũ là tự lái.

Kiểm chứng: 244 backend tests liên quan đạt trên PostgreSQL 18 riêng, gồm DB transaction, HTTP envelope/auth/validation/Swagger, lifecycle, assignment, search và editing. Sửa hai kỳ vọng test cũ: chụp readiness sau khi seed evidence, và mã lỗi immutable hiện tại do migration 0018 định nghĩa. Chưa deploy hoặc áp dụng migration lên database ứng dụng.
