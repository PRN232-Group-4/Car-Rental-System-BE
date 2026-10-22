# Trả loại thuê trong hàng đợi vận hành

- GET `/api/rentals` trước đây không có `rentalType` trong summary, khiến giao diện mặc định mọi đơn thành dài hạn dù chi tiết là theo giờ.
- Bổ sung `OperationalRentalSummary.RentalType`, lấy từ `rentals.rental_type` trong cùng query phân trang. Không thêm truy vấn mỗi đơn, không đổi bộ lọc/quyền/pagination. Handover summary dùng chung cũng nhận trường mới.
- Đồng bộ Swagger và API docs. Test SQL tạo cả đơn theo giờ/dài hạn và so với chi tiết; HTTP kiểm tra trường, envelope và Swagger.
- Kiểm chứng: **41/41 test đạt, 0 skipped**, gồm RentalOperationsApiTests, RentalQueryPaginationIntegrationTests, HandoverServiceTests; build Release thành công. PostgreSQL 18 riêng tại port 55439, mỗi fixture tạo/xóa database tạm, không dùng dữ liệu ứng dụng.
- Không migration. Chưa deploy; backend đang chạy cần khởi động lại để response có trường mới.
