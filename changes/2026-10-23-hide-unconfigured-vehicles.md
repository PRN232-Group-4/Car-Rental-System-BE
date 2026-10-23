# 2026-10-23 — Ẩn xe chưa cấu hình hạng GPLX khỏi danh mục khách hàng

## Hành vi

Public `GET /api/vehicles/search` chỉ trả xe có ít nhất một hạng `allowedLicenseTypes` khác khoảng trắng. Điều kiện SQL chạy trước count, limit và offset; xe có mảng rỗng, NULL hoặc chỉ phần tử rỗng/khoảng trắng/NULL không xuất hiện. Áp dụng cho duyệt mặc định, bộ lọc cơ sở/loại xe, thuê giờ/dài hạn và tự lái/có tài xế. Landing, tìm xe và chọn xe khi đặt thuê đều đã dùng API này nên không cần lọc lại trong FE.

Xe vẫn có trong `GET /api/vehicles`, chi tiết xe và lịch sử đơn thuê để nhân viên sửa cấu hình và tra cứu. Không thay eligibility GPLX của đơn hiện hữu, suy diễn hạng bằng tương đương, xóa dữ liệu hoặc sửa GPS `tracking/vehicles/nearby` (danh sách vận hành riêng).

## Kiểm chứng

- PostgreSQL integration kiểm tra đủ 4 phân khúc, mảng trống/khoảng trắng/NULL element, cấu hình hỗn hợp có hạng hợp lệ, tổng sau lọc, từng trang/trang rỗng, duyệt mặc định và khoảng thuê.
- Giữ xe trong quản trị/chi tiết/lịch sử và xuất hiện lại sau khi cấu hình.
- Fixtures catalog/rental-status được cấu hình hạng rõ ràng để tiếp tục kiểm tra đúng mục đích cũ. Helper subscription self-drive dùng GPLX approved theo invariant hiện tại, không thay nghiệp vụ hoặc các ca kiểm tra bằng pending riêng.
- HTTP kiểm tra envelope/pagination và Swagger mô tả điều kiện mới.
- Build vào artifacts riêng để không đụng DLL WebApi local đang chạy; PostgreSQL dùng cluster test localhost riêng và database tạm do fixture tạo. Không migration hoặc deploy DB ứng dụng.

Build tests `-warnaserror` đạt 0 lỗi/0 cảnh báo; 77/77 kiểm thử search/catalog/Application/HTTP/Swagger/inspection liên quan đạt. Test cluster được dừng sau khi chạy. WebApi local đang chạy giữ bản build cũ; cần nạp lại backend khi triển khai thay đổi.
