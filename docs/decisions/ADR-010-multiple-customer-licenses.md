# ADR-010 — GPLX độc lập và cấu hình hạng theo xe

Status: Accepted — 2026-10-23

Một khách có thể có nhiều bằng với số, hạng, ảnh, ngày và kết quả duyệt độc lập. Lưu trong customer_licenses; cột customers.license_* là projection tương thích hồ sơ đang chọn. Migration chuyển dữ liệu cũ không thay đổi duyệt. Các lần sửa giữ ID và đặt pending chỉ bằng được sửa. API cũ được mở rộng bằng licenses aggregate và LicenseId tùy chọn, không tạo endpoint list mới.

Xe lưu allowed_license_types text[]. So khớp hạng chuẩn hóa chính xác; không tự suy ra luật tương đương pháp lý. Mảng rỗng giữ tương thích xe cũ (bất kỳ bằng approved còn hạn), không nghĩa là miễn GPLX. Quyền tự lái yêu cầu ít nhất một bằng approved có hạn đến hết chuyến. Kiểm tra lại tại transaction đặt/sửa/phân công/đổi xe/bàn giao. Thuê có tài xế bỏ qua điều kiện GPLX khách.
