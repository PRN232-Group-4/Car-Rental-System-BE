# Thay đổi: Vòng đời đơn thuê, Quá giờ & Quyết toán tài chính (FR-018 / BE-17)

- **Ngày:** 2026-10-06
- **Module:** BE-17 (FR-018: Vòng đời đơn thuê — hủy, hoàn tất, quá giờ, quyết toán)
- **Nhánh:** `dev-huy`
- **Tác giả:** Antigravity / Huy

## 1. Bối cảnh & Mục tiêu
Hiện thực đầy đủ quy trình quản lý vòng đời đơn thuê xe theo đặc tả `specs/FR-018-rental-lifecycle.md`:
- Hỗ trợ các hành động chuyển đổi trạng thái: xác nhận đơn (`confirm`), bắt đầu thuê (`start`), trả xe (`return`), hoàn tất đơn kèm quyết toán (`complete`), hủy đơn (`cancel`) và xem trước phí hủy (`cancellation-preview`).
- Hỗ trợ tra cứu đơn thuê chi tiết cho cả khách hàng (`GET /api/rentals/mine`, `GET /api/rentals/mine/{id}`) và nhân viên vận hành (`GET /api/rentals`, `GET /api/rentals/{id}`, `GET /api/rentals/overdue`).
- Tính toán phí quá giờ theo cơ chế ân hạn 15 phút, làm tròn lên theo giờ (`ceil`), nhân hệ số phụ thu quá giờ và thuế VAT.
- Quyết toán tài chính tập trung một lần duy nhất tại `complete`: bù trừ cọc với phí quá giờ, tự động tạo thanh toán hoàn cọc nếu cọc dư, hoặc tạo hóa đơn phát sinh loại `overtime` nếu cọc thiếu.
- Gỡ bỏ hoàn toàn logic tự động hoàn cọc tại `AddInspectionAsync` (BE-16 / O2) để loại bỏ rủi ro thất thoát tài chính.
- Bảo vệ chống tranh chấp đồng thời (concurrency) bằng transaction kết hợp `SELECT pg_advisory_xact_lock(rentalId)` và `SELECT status FROM rentals FOR UPDATE`.

## 2. Các thay đổi chi tiết

### 2.1. Cơ sở dữ liệu (Migration `0013_rental_lifecycle.sql`)
- Cập nhật ràng buộc `ck_rentals_status`: chuẩn hóa từ vựng `pending`, `pre_inspection`, `renting`, `post_inspection`, `completed`, `cancelled`.
- Bổ sung các cột vòng đời cho bảng `rentals`: `confirmed_at`, `started_at`, `returned_at`, `completed_at`, `cancelled_at`, `cancelled_by`, `cancel_reason`, `overtime_hours`, `status_changed_at`.
- Thêm ràng buộc `ck_rentals_cancelled_by`: `customer`, `staff`, `system`.
- Tạo bảng `rental_status_history` lưu vết lịch sử chuyển đổi trạng thái kèm trigger `trg_rsh_immutable` chặn sửa/xóa.
- Tạo bảng `rental_settlements` lưu vết kết quả quyết toán cọc và quá giờ.
- Bổ sung cột `kind` (`rental`, `overtime`) trên bảng `invoices` và partial index `ux_invoices_rental_kind`.

### 2.2. Tầng Domain (`VehicleRental.Domain/Lifecycle`)
- `RentalStatus`: enum mô tả các trạng thái chuẩn.
- `RentalTrigger`: các sự kiện kích hoạt (`Confirm`, `Start`, `Return`, `Complete`, `Cancel`).
- `RentalStateMachine`: máy trạng thái hữu hạn kiểm tra tính hợp lệ của mọi bước chuyển.
- `OvertimeCalculator`: tính toán phút trễ, giờ quá giờ (áp dụng 15 phút grace period), tiền quá giờ và thuế VAT.
- `CancellationPolicy`: tính toán số tiền phạt hủy (20% phí thuê nếu hủy dưới 24h) và số tiền hoàn lại.
- `SettlementCalculator`: tính toán bù trừ cọc giữ với tiền quá giờ, xác định số tiền hoàn cọc hoặc số tiền thiếu.
- `MoneyRounding`: làm tròn tiền VND tại một chỗ duy nhất bằng `MidpointRounding.AwayFromZero`.

### 2.3. Tầng Application (`VehicleRental.Application/Lifecycle`)
- `RentalOptions`: cấu hình `GraceMinutes = 15`.
- `RentalCancellationOptions`: cấu hình `FreeCancellationWindowHours = 24`, `LateCancellationPenaltyRate = 0.20`.
- Các cổng phụ thuộc: `IRentalLifecycleRepository`, `IRentalPaymentGate`, `IInvoiceGateway`, `ISubscriptionUsage`, `ILoyaltyAward`, `IRentalEventSink`, `ICurrentCustomer`, `IRentalQueries`.
- `RentalLifecycleService`: điều phối toàn bộ luồng nghiệp vụ xác nhận, bắt đầu, trả xe, hoàn tất/quyết toán, hủy đơn và xem trước chính sách hủy.

### 2.4. Tầng Infrastructure (`VehicleRental.Infrastructure/Lifecycle`)
- `NpgsqlRentalLifecycleRepository`: thực thi các thao tác đọc/ghi trạng thái trên PostgreSQL, áp dụng `pg_advisory_xact_lock` và `SELECT FOR UPDATE` trong transaction.
- `RentalPaymentGateAdapter`: tương tác với bảng `invoices` và `payments`, tạo thanh toán hoàn cọc bảo đảm tính lũy nghiệm (idempotency) dưới điều kiện đua tranh.
- `InvoiceGatewayAdapter`: tạo hóa đơn phát sinh quá giờ `kind = 'overtime'`.
- `SubscriptionUsageAdapter`: khấu trừ giờ thừa của gói thuê bao khi trả xe trễ.
- `LoyaltyAwardAdapter`: hook sẵn sàng tích điểm khi hoàn tất đơn.
- `NpgsqlRentalQueries`: thực thi các truy vấn đọc phân trang, chi tiết và danh sách đơn trễ hạn (`overdue`), bảo vệ trước các trường hợp dữ liệu xe chưa có nhãn hiệu/model.

### 2.5. Tầng WebApi (`VehicleRental.WebApi`)
- `RentalLifecycleController`: cung cấp các endpoints chuyển đổi trạng thái với Swagger documentation đầy đủ.
- `RentalQueriesController`: cung cấp các endpoints tra cứu cho khách và nhân viên.
- `ApiSuccessResponseFilter`: bổ sung 11 message mapping chuẩn hóa cho các API vòng đời và tra cứu.
- `OperationsController.Status`: đánh dấu `[Obsolete]` và trả response header `Deprecation: true`.
- `Program.cs`: đăng ký DI đầy đủ cho các options và services mới.

### 2.6. Gỡ bỏ Auto-refund tại Inspection (BE-16 / O2)
- Gỡ bỏ câu lệnh tự động tạo refund payment tại `AddInspectionAsync` trong `NpgsqlOperationsRepository.cs`.
- Thêm xử lý hoàn tất đơn ở `ChangeRentalStatusAsync("completed")` tương thích với test cũ.
- Cập nhật test cũ `PaymentTests.cs` để test xanh 100%.

## 3. Kiểm thử & Đánh giá
- **Tổng số test pass:** 318/318 tests (100% xanh, 0 warning, 0 error).
- **Domain tests:** 51 unit tests bao phủ toàn bộ ma trận 6 trạng thái x 5 lệnh, các mốc thời gian biên, và công thức tính cọc/quá giờ.
- **Service unit tests:** 17 unit tests với mock adapters.
- **Schema integration tests:** 17 integration tests kiểm tra trigger bất biến, ràng buộc check, partial index trên PostgreSQL Neon thật.
- **Integration & Concurrency tests:** 5 kịch bản tích hợp hoàn chỉnh:
  - Kịch bản E2E: Tạo đơn → Xác nhận → Kiểm định trước → Bắt đầu → Trả trễ 90 phút → Quyết toán → Hoàn cọc dư 280k → Kiểm tra read model.
  - Kịch bản Shortfall: Quá giờ vượt tiền cọc → Tạo hóa đơn `kind = 'overtime'` 340k với hạn 72 giờ.
  - Kịch bản Concurrency: 20 lần gọi song song `CompleteAsync` đồng thời → đúng 1 request thành công, 1 request nhận mã lỗi `ALREADY_IN_STATE`, đúng 1 bản ghi quyết toán và 1 bản ghi hoàn cọc trong DB.
  - Kịch bản Hủy đơn trước 24h: Hoàn 100% tiền thuê và cọc.
  - Kịch bản Hủy đơn dưới 24h: Giữ lại 20% phí phạt tiền thuê, hoàn 80% tiền thuê + 100% cọc.
