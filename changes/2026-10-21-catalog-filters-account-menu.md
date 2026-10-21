# Sửa bộ lọc danh mục thuê xe và menu tài khoản

Nguồn: [FR-010](../specs/FR-010-search-availability.md), [API](../docs/API.md).

- Nguyên nhân: `/find` gửi tiêu chí tới API danh sách cơ bản, nhiều query bị bỏ qua; hàm API frontend chưa chuyển ngày giờ/hình thức thuê.
- FE dùng `/api/vehicles/search`, ánh xạ seats/giá/từ khóa/sort sang đúng contract, ánh xạ nested type/facility/summary. Ngày + giờ từ widget trang chủ được chuyển GMT+7 → UTC; picker hourly nhận tối thiểu 1 giờ; chip lịch xóa cả hai mốc. Tham số URL sai được xử lý từng trường, giữ tiêu chí hợp lệ. Giá dài hạn dùng đơn vị tháng.
- BE thêm `category`, `name_asc`, trả `vehicleType.type`, hỗ trợ tìm hãng + mẫu/năm/chi nhánh và chuẩn hóa hộp số/nhiên liệu (automatic bao gồm CVT). Tất cả tiêu chí/sort áp dụng trước pagination/count; giữ đúng tổng cả trang rỗng. Không migration.
- Account menu desktop/mobile, customer/admin/driver thêm **Đổi mật khẩu** dẫn tới form OTP hiện có trong hồ sơ. Anchor chờ form tải xong để cuộn đúng vị trí. Giữ workflow OTP và thu hồi phiên của backend.
- Giả định: `seatCount` tiếp tục là số chỗ tối thiểu theo contract cũ, giao diện ghi rõ. Loại/nhiên liệu lấy metadata và chi tiết xe sẵn có, không hardcode ID loại xe.

## Kiểm chứng

- Backend build với `-warnaserror`: 0 warning/error. 54/54 test Application/HTTP/API contract/PostgreSQL tìm xe đạt, gồm tổ hợp lọc, bốn phân khúc, dữ liệu nhiên liệu cũ, giá giờ/tháng, sort, tổng sau lọc/trang rỗng và lịch thuê/bảo dưỡng. PostgreSQL 18 tạm trên loopback, database riêng từng fixture, đã dừng sau kiểm thử.
- FE 662/662 unit test đạt; TypeScript, ESLint file thay đổi, design lint và production static build (258 trang) đạt. Lượt chạy toàn bộ Vitest trong sandbox lỗi cache tạm; chạy ngoài sandbox với 2 worker đạt. Không đổi cấu hình test của repo. jsdom có log giới hạn điều hướng nhưng không test nào thất bại.
- Playwright Edge: 12/12 desktop/mobile đạt, gồm mọi control, giữ/xóa/reset tiêu chí, chuyển GMT+7, mở form OTP từ account menu và route Admin/Staff/Staff+Driver. Ba test bộ lọc chạy lại sau đồng bộ giờ từ URL đều đạt. PostgreSQL tạm đã dừng và xóa sau kiểm thử.

Chưa deploy hoặc gửi OTP thật. Cần triển khai cả FE/BE; dev FE mặc định proxy tới Railway, nên backend Railway phải được cập nhật hoặc `DEV_API_ORIGIN` trỏ backend local tương ứng.
