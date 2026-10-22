# Phân công và xác nhận inspect sau trả xe

Mở rộng GET/PUT inspection-assignment và POST checked với query inspectionType mặc định pre_rental, hỗ trợ post_rental. HTTP/Application xác minh loại; giai đoạn sau chỉ ghi ở post_inspection, kỹ thuật viên Maintenance active, không có evidence sau thuê hiện hữu. Admin checked lưu assignment và biên bản đạt cùng transaction, ghi provenance, không tự complete/payment. Manager chỉ phân công chờ kỹ thuật, biên bản đầy đủ hiện hữu tiếp tục hỗ trợ không đạt/hư hỏng.

Migration 0029 giữ phân công trước thuê cũ và thêm khóa theo loại kiểm tra. Lock điều phối và rental dùng chung với nghiệp vụ inspect/đổi xe hiện tại, kiểm tra evidence trước ghi, không ghi đè cả biên bản không đạt. Pre API/approveRequest giữ tương thích. Swagger/docs/API cập nhật query và message sau trả riêng.

Kiểm chứng: build Release và 54 tests liên quan đạt trên PostgreSQL cục bộ; bao gồm hai giai đoạn độc lập, wrong-stage, evidence không đạt, concurrency một lần ghi, quyền Admin/Manager, invalid query, envelope HTTP và Swagger. Chưa deploy; cần chạy backend mới/migration 0029 cùng FE.
