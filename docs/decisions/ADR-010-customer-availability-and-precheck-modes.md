# ADR-010 — Xe customer và các cách xác nhận trước thuê

Cập nhật cùng ngày theo yêu cầu: customer chủ đơn và Admin có quyền hủy ở pending/pre_inspection, trước renting; quá giờ nhận dự kiến chưa bắt đầu thuê vẫn có thể hủy với phí muộn hiện có. Thay thế giới hạn customer chỉ pending/trước giờ nhận trước đây; preview, cờ canCancel và cancel command phải đồng bộ, không suy diễn miễn phí hủy.

- Ngày: 2026-10-22
- Trạng thái: Accepted — yêu cầu trực tiếp của người dùng

Customer chỉ được liệt kê xe available và không có rental đang renting/post_inspection; không mở lại xe đang thuê chỉ vì khách chọn ngày tương lai. Pending/pre_inspection vẫn giữ lịch theo vehicle_bookings/đệm. Backend tạo đơn kiểm tra lại trong transaction dưới vehicle lock; Admin giữ danh mục đầy đủ.

Cả thuê giờ/dài hạn lưu lựa chọn có tài xế hoặc tự lái. Đơn có tài xế được mặc định đạt kiểm tra trước thuê, còn tự lái cần kiểm tra kỹ thuật hoặc Admin xác nhận phân công và kiểm tra đạt. Admin phải chọn kỹ thuật viên Maintenance active; Manager được phân công nhưng không có endpoint xác nhận trực tiếp. Thay thế việc FE cố định có tài xế và longterm ép tự lái.

Giữ một nguồn readiness hiện có là inspections của rental/xe hiện tại. Bổ sung confirmation_source/confirmed_by để phân biệt technician, driver_default/system và admin_confirmation/principal. Xác nhận mặc định/Admin để trống số đo/ảnh; không giả dữ liệu kiểm tra thực tế. Không ghi đè evidence đã có, kể cả fail. Trigger tạo mặc định trong cùng transaction khi tạo/đổi xe và backfill đơn chưa bắt đầu. Evidence kỹ thuật/Admin giữ bất biến; điều khoản chưa cam kết có thể đổi về tự lái và xóa xác nhận mặc định không còn áp dụng.

Mọi cách xác nhận chỉ đáp ứng bước kiểm tra trước thuê. Điều kiện assignment xe/tài xế thật, thanh toán, GPLX tự lái và lịch/bảo dưỡng vẫn được kiểm tra trước bắt đầu. Kiểm tra sau thuê, giờ trả, phí trễ/cọc giữ nguyên.
