# Danh sách Admin mới nhất trước

- Xe: sắp ngày tạo giảm dần, ID giảm dần khi trùng ngày. Customer/GPLX, cơ sở, vai trò và nhân viên: sắp ID giảm dần vì không có ngày tạo hồ sơ riêng trong schema.
- SQL ORDER BY chạy trước LIMIT/OFFSET; giữ bộ lọc, count, validation và envelope. Nhân viên mới được thêm không còn bị đẩy xuống do ngày vào làm cũ hoặc tên.
- Các danh mục legacy phòng ban/loại xe/gói trả toàn bộ array được FE sắp đầy đủ theo ID giảm dần trước chia trang. Không thay contract các API này. Các danh sách vốn newest-first giữ quy tắc hiện tại; lịch/ca và dashboard giữ thứ tự nghiệp vụ.
- Không migration hoặc cập nhật dữ liệu. Cần khởi động lại/deploy backend để áp dụng SQL mới.
- Kiểm chứng: 74/74 backend tests liên quan đạt trên PostgreSQL 18 riêng, gồm thứ tự global/phân trang/filter/empty và HTTP/envelope; frontend có kiểm thử danh mục và nhân viên. Swagger ghi nhận thứ tự mới.
