# Đặt xe theo cơ sở và email OTP Thong Dong

- `vehicles/search?hasDriver=true&facilityId=...` kiểm tra tài xế thuộc đúng cơ sở, active, GPLX hợp lệ và rảnh trong cả khoảng thuê có đệm. Dùng period assignment mới, có fallback cho bản ghi cũ chưa có period; không tự gán hoặc giữ chỗ tài xế khi tìm xe.
- Search validate pagination tại HTTP/Application, mặc định 1/20, pageSize tối đa 100, offset Int64; trang ngoài phạm vi giữ totalCount. Bổ sung message mappings cho VehicleSearch và Swagger/API docs.
- Brevo OTP nhận HTML responsive nền ấm/slate/amber theo DESIGN.md frontend, mã nổi bật, tên/mã escape HTML và text fallback. Dùng cho cả đăng ký/reset; giữ giao thức gửi, TTL/cooldown và lỗi provider an toàn.
- Kiểm chứng: build `-warnaserror` không warning/error; 55 test liên quan tìm xe, Application, HTTP/Swagger, PostgreSQL18 tạm và Brevo HTTP giả đạt. Preview email desktop/390px mobile được kiểm tra, không tràn ngang. Không gửi email thật hoặc deploy.
