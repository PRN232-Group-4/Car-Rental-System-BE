# FR-032 — Danh sách và chi tiết khách hàng

- **Status:** Done; **Priority:** P1; **Milestone:** M2
- **Change:** [2026-10-10-customer-directory-media.md](../changes/2026-10-10-customer-directory-media.md)

## API và phân quyền

`GET /api/customers` và `GET /api/customers/{customerId:int}` dùng policy `CustomerService`: Admin, Manager và nhân viên CustomerService được xem. Customer, Driver, Maintenance và Staff không có specialty phù hợp không được truy cập danh bạ.

Danh sách nhận `search` (tối đa 255 ký tự), `isActive` nullable, `page` (mặc định 1, tối thiểu 1) và `pageSize` (mặc định 20, từ 1 đến 100). Tìm kiếm không phân biệt hoa/thường theo tên, email hoặc điện thoại; khoảng trắng đầu/cuối được bỏ; `%` và `_` là ký tự tìm kiếm thông thường. Khi không truyền `isActive`, trả cả khách active và inactive. Kết quả gồm `items`, `page`, `pageSize`, `totalCount`, sắp theo customer ID tăng dần và trả trang rỗng nếu vượt tổng số trang.

Danh sách chỉ chọn khách có bản ghi `customers` nối tới `users` tồn tại. Mỗi item gồm `id`, `userId`, `email`, `fullName`, `phone`, `avatarUrl`, `isActive`, `licenseStatus`, `loyaltyPoint`.

Chi tiết sử dụng **customers.id**, không phải `users.id`. ID dương trả thông tin profile (`id`, `userId`, `email`, `fullName`, `gender`, `phone`, `avatarUrl`, `isActive`), `license` (number/type/issueDate/expiryDate/imageUrl/status) và `loyalty.points`. Khách inactive vẫn xem được. ID không dương trả 400; không có khách hoặc user tham chiếu không tồn tại trả 404. Không trả password hash, authorization version, session hoặc token.

## Acceptance criteria

- [x] API list/detail xuất hiện trên Swagger với query, schema, status và quyền rõ ràng.
- [x] List có phân trang ổn định, tìm tên/email/điện thoại, lọc active và xử lý trang ngoài phạm vi.
- [x] Validation pagination/search được áp dụng tại HTTP và Application; offset không tràn số nguyên khi page lớn.
- [x] Chi tiết dùng customer ID, trả profile/license/loyalty và xử lý 400/404 như trên.
- [x] Admin/Manager/CustomerService được xem; user không đăng nhập nhận 401 và các role khác nhận 403.
- [x] JSON thực tế có đủ 6 trường envelope, kể cả null; boolean `is_success`, code khớp HTTP, không bọc hai lần.
- [x] Success message đúng ngữ cảnh: `Customers retrieved successfully.` và `Customer retrieved successfully.`
- [x] Có test Application, PostgreSQL và HTTP; không lộ dữ liệu xác thực.
