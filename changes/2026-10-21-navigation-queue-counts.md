# Tổng việc chờ xử lý trên điều hướng — 2026-10-21

Thêm `GET /api/admin/navigation-counts` trả snapshot năm queue rentals/inspections/maintenance/reports/licenses trong envelope chuẩn. Đếm trên server bằng một câu SQL, không cộng phân trang. GPLX chưa gửi bị loại; inspection chỉ đếm đơn chưa có biên bản đúng loại và xe hiện tại. Phiếu bảo trì đếm open/scheduled, báo cáo đếm open.

Quyền theo API nghiệp vụ: rentals/licenses cho Admin/Manager/CustomerService, inspections cho các role đó và Maintenance, maintenance cho Admin/Manager/Maintenance. Reports toàn hệ thống cho Admin/Manager; nhân viên khác chỉ đếm báo cáo của staff_id từ claim. Staff chỉ nhận reports của mình. Các tổng không có quyền trả 0. Các queue khác cùng phạm vi toàn hệ thống của danh sách hiện tại; không thêm phân vùng cơ sở. “Mới” là chờ xử lý, chưa triển khai mốc đọc cá nhân.

FE hiển thị dot góc phải mục cha, số chính xác ở mục con và đường dẫn GPLX chờ duyệt. Poll 30 giây khi đang mở, refetch sau mutation thành công, giữ quyền menu và không dựng badge giả khi API lỗi.

Kiểm chứng: 13/13 test navigation đạt (12 HTTP/Swagger/RBAC/envelope và 1 PostgreSQL), thuộc lượt tổng hợp 154/154 test đạt. Bao phủ scope báo cáo theo người tạo, queue không có quyền, GPLX trống/orphan, biên bản xe cũ và giảm tổng khi xử lý xong. FE E2E Edge đạt 8/8 với API giả lập, gồm desktop/mobile và sáng/tối. Không migration hoặc triển khai; chưa đối chiếu dữ liệu ứng dụng thật.
