-- Bỏ chức năng ca trực tài xế: phân công tài xế chỉ còn dựa trên tài xế đang hoạt động, GPLX còn hạn và
-- không trùng chuyến khác. Xóa bảng driver_shifts cùng dữ liệu ca đã tạo (FK fk_driver_shifts_driver_staff_id
-- của 0034 đi theo bảng). Không có bảng nào tham chiếu tới driver_shifts.
DROP TABLE IF EXISTS driver_shifts;
