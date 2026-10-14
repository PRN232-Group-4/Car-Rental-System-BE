# ADR-002 — Cơ chế vehicle_bookings, exclusion constraint và đệm đối xứng 60 phút
- Status: Accepted
- Ngày: 2026-10-04

## Bối cảnh
Hệ thống cho thuê xe cần ngăn chặn tình trạng đặt trùng lịch (double booking) và phải đảm bảo một khoảng đệm an toàn (buffer) giữa các lượt thuê để kiểm tra, bàn giao và vệ sinh xe.
Yêu cầu:
1. Đảm bảo tính nhất quán tuyệt đối kể cả khi có nhiều request đồng thời (race condition / concurrency).
2. Thời gian đệm mặc định là 60 phút. Khoảng đệm phải mang tính đối xứng: lượt thuê mới không được đè vào đệm sau của lượt thuê trước, và kết thúc của lượt thuê mới cũng không được đè vào bắt đầu của lượt thuê sau.
3. Không để logic khóa hay exclusion constraint bị vượt qua; nếu ứng dụng lách advisory lock thì database vẫn phải từ chối.

## Quyết định
1. **Bảng `vehicle_bookings` và Generated Column**:
   - Thêm bảng `vehicle_bookings` đồng bộ tự động từ `hour_rental_detail` và `longterm_rental_contract` thông qua các trigger PostgreSQL.
   - Thêm cột generated lưu khoảng thời gian bao gồm cả đệm:
     `period tsrange GENERATED ALWAYS AS (tsrange(starts_at, ends_at + make_interval(mins => buffer_minutes), '[)')) STORED`.
2. **Lưới bảo vệ cuối cùng (PostgreSQL Exclusion Constraint)**:
   - Sử dụng extension `btree_gist` và ràng buộc loại trừ:
     `CONSTRAINT ex_vehicle_bookings_no_overlap EXCLUDE USING gist (vehicle_id WITH =, period WITH &&) WHERE (is_blocking)`.
   - Bất kỳ xung đột nào lách qua tầng ứng dụng đều bị PostgreSQL quăng mã lỗi `SQLSTATE 23P01`, được ứng dụng chuyển đổi thành `ConflictException("VEHICLE_UNAVAILABLE")` (HTTP 409).
3. **Cơ chế hai lớp bảo vệ (Two-Layer Defense)**:
   - Tầng ứng dụng: Khóa advisory xact lock `pg_advisory_xact_lock(1001, vehicle_id)` và kiểm tra `AssertAvailableAsync` trước khi insert.
   - Tầng cơ sở dữ liệu: Exclusion constraint loại trừ việc chồng lấn thời gian.
4. **Kiểm tra Availability với Đệm đối xứng**:
   - Khi tìm kiếm hoặc kiểm tra xe, khoảng thời gian yêu cầu `[from, to)` được mở rộng đối xứng `[from - buffer, to + buffer)` để đối chiếu với các booking hiện có:
     `period && tsrange(from - buffer, to, '[)')` và `starts_at < to + buffer`.

## Hệ quả
1. Triệt tiêu hoàn toàn khả năng xung đột lịch thuê ngay cả dưới tải concurrency cao.
2. Việc thay đổi giá trị đệm mặc định sau này sẽ đòi hỏi migration cập nhật lại cột `buffer_minutes` và tính lại `period` cho các booking đang chặn.
3. Cần đảm bảo tham số `timestamp` truyền vào Npgsql tuân thủ `DateTimeKind.Unspecified` để tương thích hoàn toàn với Npgsql 8.0.
