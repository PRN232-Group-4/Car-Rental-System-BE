# Lọc khách hàng theo trạng thái GPLX

- Thêm query `licenseStatus=pending|approved|rejected|none` cho `GET /api/customers`, kiểm tra tại HTTP và Application. Giữ phân quyền CustomerService (Admin/Manager/CustomerService) và contract envelope/phân trang hiện tại.
- Truy vấn PostgreSQL kết hợp tìm kiếm, tài khoản và trạng thái trước phân trang/tính tổng; thứ tự customer ID ổn định, offset Int64.
- Danh sách trả `none` nếu số GPLX null/rỗng/chỉ khoảng trắng, phân biệt khách chưa cập nhật với hồ sơ `pending` đã gửi chờ duyệt. Không sửa dữ liệu hoặc trạng thái lưu trong DB.
- Cập nhật FR-032, docs/API.md và mô tả/query enum trong Swagger response operation filter.
- Kiểm chứng: build khi chạy test với `-warnaserror` đạt; 35/35 test CustomerList Application/HTTP/Swagger/PostgreSQL đạt trên PostgreSQL 18 tạm ở localhost, không sử dụng dữ liệu thật.
- Frontend cùng workspace có mục Khách hàng, bộ lọc GPLX server-side; phần kiểm tra GPLX trước đặt xe và thẻ xe được ghi nhận tại change frontend ngày 2026-10-18. Chưa deploy.
