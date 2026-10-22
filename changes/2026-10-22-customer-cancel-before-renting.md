# Customer và Admin hủy trước Đang thuê

Customer chủ đơn được hủy ở pending/pre_inspection, kể cả đã qua giờ nhận dự kiến nhưng chưa renting, theo yêu cầu người dùng. Giữ chính sách phí hủy muộn/hoàn tiền, ownership và transaction lock. Mine list/detail canCancel và preview đồng bộ trạng thái; kiểm tra lại trước cập nhật. Có kiểm thử policy/service/DB cho pre_inspection, nhận trễ, phí/hoàn và chặn sau renting.

Lỗi inspect Not Found là sai khác phiên bản triển khai: đã đọc Swagger Railway và xác nhận `/inspection-assignment` có nhưng `/inspection-assignment/checked` chưa có. Source controller mới có route, phân quyền Admin, validation và transaction đã được kiểm thử. Cần triển khai source backend cùng migration 0028 từ thay đổi trước; không sửa endpoint thành một đường ghi evidence khác hoặc tự giả thành công. Chưa deploy hay migrate database ứng dụng.

Kiểm chứng: 148 backend tests liên quan đạt trên PostgreSQL 18 riêng, gồm policy/service, DB, HTTP/envelope/Swagger. Ba ca pre_inspection trước 30h, trước 10h và sau giờ nhận 1h xác minh preview/cờ canCancel/phí/hoàn tiền và trạng thái sau hủy. FE 53 unit/integration và 38 E2E desktop/mobile đạt.
