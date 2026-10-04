# FR-015 — Pre-rental inspection
- **Status:** Implemented; **Priority:** P0; **Milestone:** M4

Một pre-inspection/rental/vehicle, evidence bất biến; is_ok=true của xe hiện tại mới được renting. Fail giữ pending, khóa xe để Manager thay xe cùng số chỗ; không tự hủy rental, tạo bảo trì hoặc tính phí.
Admin/Maintenance có staff profile thực hiện. staffId trong body phải trùng claim; luôn lưu người thực hiện từ principal. Xe thay thế có endpoint inspection riêng. Xem FR-031.
