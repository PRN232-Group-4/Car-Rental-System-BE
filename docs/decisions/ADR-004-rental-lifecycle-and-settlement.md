# ADR-004 — Vòng đời đơn thuê, quyết toán cọc một lần và kiểm soát tranh chấp trạng thái
- Status: Accepted
- Ngày: 2026-10-06

## Bối cảnh
Hệ thống quản lý thuê xe VRS (Thong Dong) cần chuẩn hóa toàn diện vòng đời đơn thuê và quyết toán tài chính:
1. Quy trình trạng thái đơn thuê từ lúc khách đặt đến khi hoàn tất hoặc hủy: `pending` → `pre_inspection` → `renting` → `post_inspection` → `completed` (hoặc `cancelled` từ các bước trước khi bắt đầu thuê).
2. Xử lý trả xe trễ giờ (quá giờ): Dung sai 15 phút ân hạn, làm tròn lên theo giờ (`Math.Ceiling`), tính thuế VAT tương ứng và khấu trừ vào tiền cọc đã giữ.
3. Giải quyết mâu thuẫn hoàn cọc (O2): Trước đây `AddInspectionAsync` tự động tạo yêu cầu hoàn cọc 100% ngay khi nhân viên kiểm định xong, dẫn đến rủi ro hoàn cọc trước khi tính toán phí phạt/quá giờ, gây thất thoát tài chính và hoàn cọc trùng lặp.
4. Xử lý hủy đơn (cancellation policy): Phân định rõ thời điểm trước 24 giờ (miễn phí hủy, hoàn 100%) và dưới 24 giờ (giữ phí phạt 20% tiền thuê, hoàn 80% + 100% cọc). Khi nhân viên hoặc hệ thống hủy: hoàn 100%.
5. Kiểm soát tranh chấp (concurrency): Ngăn chặn việc gọi đồng thời hai yêu cầu `complete` hoặc `cancel` dẫn đến tạo trùng lặp hóa đơn quá giờ, hoàn tiền cọc hai lần, hoặc giải phóng xe sai trạng thái.

## Quyết định
1. **Máy trạng thái đơn thuê (Domain State Machine) thuần túy**:
   - Quản lý từ vựng trạng thái chuẩn: `pending`, `pre_inspection`, `renting`, `post_inspection`, `completed`, `cancelled`.
   - Mọi bước chuyển trạng thái tuân thủ nghiêm ngặt máy trạng thái hữu hạn trong Domain (`RentalStateMachine`), cấm nhảy cóc hoặc đảo ngược quy trình.
   - Bảng `rental_status_history` đóng vai trò là sổ cái lưu vết bất biến toàn bộ lịch sử chuyển trạng thái, kèm trigger PostgreSQL (`trg_rsh_immutable`) chặn hoàn toàn mọi lệnh `UPDATE` hoặc `DELETE`.

2. **Quy tắc tính toán quá giờ và làm tròn tiền tệ tập trung**:
   - `OvertimeCalculator` áp dụng 15 phút ân hạn (`grace_minutes = 15`). Nếu trễ trong vòng 15 phút: tính 0 giờ quá giờ. Nếu trễ vượt 15 phút: làm tròn lên theo giờ nguyên (`ceil(phút / 60)`).
   - Đơn giá giờ quá giờ lấy từ `unit_hourly_price` (hoặc quy đổi từ `monthly_price_snapshot / 720` đối với đơn dài hạn), nhân với hệ số phụ thu `overtime_fee_rate` và thuế suất `vat_rate`.
   - Tất cả giá trị tiền tệ dùng kiểu `decimal` và làm tròn số nguyên VND tại một hàm duy nhất `MoneyRounding.RoundVnd(decimal)` sử dụng `MidpointRounding.AwayFromZero`.

3. **Quyết toán cọc một lần duy nhất tại `complete` (Gỡ bỏ auto-refund tại Inspection)**:
   - Gỡ bỏ hoàn toàn logic tạo thanh toán hoàn cọc tại `AddInspectionAsync`. Khâu kiểm định chỉ ghi nhận tình trạng xe (`is_ok`, số km, xăng/pin, ghi chú hư hại, ảnh chụp).
   - Việc quyết toán cọc và quá giờ được thực hiện tập trung duy nhất tại `POST /api/rentals/{id}/complete` và ghi nhận chi tiết vào bảng `rental_settlements`:
     - Nếu Tiền cọc giữ >= Tổng tiền quá giờ: Khấu trừ tiền quá giờ vào cọc, tạo một bản ghi thanh toán hoàn cọc (`payments.type = 'refund'`) cho phần tiền cọc còn dư.
     - Nếu Tiền cọc giữ < Tổng tiền quá giờ: Khấu trừ toàn bộ cọc giữ, tạo một hóa đơn phát sinh quá giờ (`invoices.kind = 'overtime'`) với số tiền thiếu (`shortfall`), kỳ hạn thanh toán 72 giờ.
   - Bảng `invoices` được bổ sung cột `kind` (`rental`, `overtime`) và partial unique index `ux_invoices_rental_kind` để chặn việc tạo trùng hóa đơn quá giờ cho cùng một đơn thuê.

4. **Kiểm soát tranh chấp đa tầng (Advisory Lock + FOR UPDATE)**:
   - Khi thực hiện các lệnh chuyển đổi trạng thái (`confirm`, `start`, `return`, `complete`, `cancel`), hệ thống mở transaction trên PostgreSQL và khóa advisory lock theo rental ID: `SELECT pg_advisory_xact_lock($1)`.
   - Tiếp tục khóa dòng kiểm tra trạng thái hiện hành: `SELECT status FROM rentals WHERE id = $1 FOR UPDATE`.
   - Nếu đơn thuê đã ở trạng thái đích (hoặc đã chuyển sang trạng thái khác), hệ thống ném ngoại lệ nghiệp vụ `ConflictException("ALREADY_IN_STATE", ...)` hoặc `ConflictException("ILLEGAL_TRANSITION", ...)`, chặn đứng việc hoàn cọc hoặc ghi sổ cái hai lần ngay cả khi có 20 request gửi đến song song.

5. **Tương thích ngược hợp đồng API**:
   - Giữ lại endpoint cũ `PATCH /api/rentals/{id}/status` của `OperationsController` dưới dạng bí danh deprecated (gắn attribute `[Obsolete]`), bổ sung response header `Deprecation: true`, ủy thác xử lý nội bộ theo các command mới.

## Hệ quả
1. Triệt tiêu hoàn toàn rủi ro thất thoát tài chính do hoàn cọc trước khi tính quá giờ hoặc hoàn cọc hai lần.
2. Đơn thuê có đầy đủ lịch sử kiểm toán bất biến, phục vụ tra cứu, đối soát kế toán và giải quyết tranh chấp.
3. Toàn bộ 318 test trong giải pháp chạy xanh 100%, kịch bản đua tranh 20 lần lặp song song vượt qua mà không có hiện tượng flaky.
