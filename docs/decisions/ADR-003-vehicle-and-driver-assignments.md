# ADR-003 — Quy định rentals.vehicle_id là xe hiện hành, exclusion tài xế và hai lớp bảo vệ khi gán/đổi xe
- Status: Accepted
- Ngày: 2026-10-04

## Bối cảnh
Hệ thống quản lý thuê xe VRS (Thong Dong) cần hỗ trợ đầy đủ các nghiệp vụ vận hành:
1. Gán tài xế hoặc cho phép khách tự lái đối với các đơn thuê.
2. Đổi tài xế khi tài xế ban đầu bận, xin nghỉ hoặc theo yêu cầu điều phối.
3. Đổi xe thay thế khi xe đang chạy gặp sự cố kỹ thuật (hỏng hóc, tai nạn), cần bảo trì khẩn cấp hoặc khách hàng yêu cầu nâng cấp/đổi xe.
4. Đảm bảo một tài xế không bị xếp lịch chồng lấn cho nhiều đơn thuê cùng lúc (có tính đệm 60 phút nghỉ ngơi/di chuyển).
5. Đảm bảo toàn bộ quy trình vận hành và tài chính (hóa đơn, kiểm định xe, định vị GPS, thanh toán hoàn cọc) luôn nhận diện đúng chiếc xe đang phục vụ mà không bị sai lệch lịch sử.

## Quyết định
1. **`rentals.vehicle_id` luôn là xe hiện hành**:
   - Trường `rentals.vehicle_id` luôn phản ánh chính xác chiếc xe đang phục vụ khách hàng tại thời điểm hiện tại.
   - Bảng `vehicle_assignments` đóng vai trò là sổ cái lưu vết toàn bộ chuỗi gán xe và tài xế trong suốt vòng đời đơn thuê.
   - Luôn duy trì bất biến: Với bản ghi `vehicle_assignments` có `status = 'assigned'`, giá trị `vehicle_assignments.vehicle_id` bắt buộc phải khớp với `rentals.vehicle_id`.

2. **Vòng đời và lưu vết trạng thái Assignment**:
   - Từ vựng trạng thái hợp lệ của `vehicle_assignments.status` được chuẩn hóa qua constraint `ck_va_status`: `assigned`, `reassigned`, `completed`, `cancelled`.
   - Khi đổi tài xế hoặc đổi xe: Bản ghi hiện hành chuyển thành `reassigned`, cập nhật thời điểm chấm dứt `ended_at = now` và thu hẹp `ends_at = LEAST(ends_at, now)`. Bản ghi mới được tạo với `status = 'assigned'`, ghi nhận `supersedes_id` trỏ về bản ghi trước và `reason` mô tả lý do thay đổi.

3. **Cơ chế hai lớp bảo vệ chống trùng lịch tài xế**:
   - **Tầng ứng dụng (Pessimistic Advisory Lock)**: Sử dụng category lock `1002` qua hàm `pg_advisory_xact_lock(1002, driverStaffId)`. Khi đổi xe liên quan đến 2 xe, ứng dụng khóa advisory lock category `1001` theo thứ tự ID tăng dần (`Math.Min(v1, v2)` trước, `Math.Max(v1, v2)` sau) để triệt tiêu nguy cơ deadlock.
   - **Tầng cơ sở dữ liệu (PostgreSQL Exclusion Constraint)**: Sử dụng extension `btree_gist` và ràng buộc loại trừ:
     `CONSTRAINT ex_va_driver_no_overlap EXCLUDE USING gist (driver_staff_id WITH =, period WITH &&) WHERE (status = 'assigned' AND driver_staff_id IS NOT NULL AND period IS NOT NULL)`.
     Cột `period tsrange` được sinh tự động có tính thêm `buffer_minutes` (mặc định 60 phút). Bất kỳ xung đột nào vi phạm exclusion constraint sẽ kích hoạt mã lỗi `23P01` và được ứng dụng bắt, ánh xạ thành HTTP 409 `DRIVER_BUSY`.

4. **Xử lý sự cố kỹ thuật và mở phiếu bảo dưỡng tích hợp**:
   - Khi đổi xe với lý do sự cố kỹ thuật (`breakdown`, `accident`):
     - Xe cũ được cập nhật trạng thái `status = 'maintenance'`.
     - Tự động mở một phiếu bảo dưỡng mới trong bảng `vehicle_maintenance` với `maintenance_type = 'incident'`, `status = 'open'`, `is_safe = false` và liên kết với `rental_id`.
   - Việc cập nhật `rentals.vehicle_id` kích hoạt trigger của BE-10 để tự động dời khoảng giữ chỗ trong bảng `vehicle_bookings` sang xe mới trong cùng một transaction.

## Hệ quả
1. Bất biến "Xe hiện hành" luôn được đảm bảo, giúp các module thanh toán cọc (BE-13), kiểm định bàn giao xe (BE-15/16) và vòng đời đơn thuê (BE-18) hoạt động chính xác.
2. Triệt tiêu hoàn toàn khả năng một tài xế bị gán trùng lịch ngay cả khi có nhiều nhân viên điều phối cùng thao tác đồng thời.
3. Không làm đứt gãy hợp đồng cũ: Endpoint `POST /api/rentals/{id}/assignments` được giữ lại dưới dạng deprecated alias, bổ sung header `Deprecation: true` và ủy quyền xử lý sang workflow chuẩn.
