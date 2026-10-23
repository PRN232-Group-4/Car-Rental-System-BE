# Tự động hết hạn yêu cầu đặt xe — 2026-10-23

## Bối cảnh

Yêu cầu đặt xe hiện giữ lịch xe vô thời hạn khi chưa được duyệt hoặc chưa thanh toán. Khách đặt rồi bỏ quên làm xe bị khóa lịch, và không có mốc thời gian nào để nhân viên biết đơn còn hiệu lực. Yêu cầu nghiệp vụ: `start_time` phải cách thời điểm đặt tối thiểu 1 giờ; trong 1 giờ kể từ lúc đặt, đơn phải được duyệt **và** thanh toán, nếu không thì tự động hết hạn (FR-042).

## Đã làm

### Domain

- `RentalStatus.Expired` + `RentalTrigger.Expire`; chuyển `pending`/`pre_inspection` → `expired`; `expired` là trạng thái kết thúc (mọi trigger khác trả `IllegalTransition`).
- `RentalExpiryRule` giữ trọn quy tắc thời gian: `IsStartTimeAllowed`, `ComputeExpireAt`, `IsPastDeadline`, `IsFulfilled(confirmed, isSubscription, invoicePaid)`.
- `InvoiceCheckoutPolicy.BlockedReason` trả `INVOICE_NOT_PAYABLE` cho đơn `expired` (đối chiếu với `cancelled`).

### Application

- `RentalExpiryOptions` (`Rental:Expiry`), `IRentalExpiryService`/`RentalExpiryService`: quét lô, chuyển trạng thái, phát `RentalStatusChangedEvent`.
- `OperationsService.CreateRentalAsync` chặn `startTime < now + MinimumLeadMinutes` bằng `BusinessRuleException("RENTAL_START_TIME_TOO_SOON")` và truyền `expireAt` xuống repository.
- `IRentalLifecycleRepository` thêm `ListExpiringRentalIdsAsync` và `UpdateToExpiredAsync`; `RentalQueryValidation` chấp nhận trạng thái/tab `expired`.
- `CustomerRentalSummary`, `CustomerRentalDetail`, `OperationalRentalSummary`, `RentalLifecycleEntity` thêm `ExpireAt`/`ExpiredAt`.

### Infrastructure

- `NpgsqlRentalLifecycleRepository.UpdateToExpiredAsync`: advisory lock + `FOR UPDATE` + kiểm tra lại điều kiện; ghi `status`, `expired_at`, `cancel_reason`, `status_changed_at`; hủy hóa đơn thuê chưa thu; ghi `rental_status_history` với actor `System`.
- `RentalExpiryHostedService` chạy mỗi phút (lượt đầu chờ hết chu kỳ để nhường migration), bỏ qua khi `Enabled=false`, log số đơn xử lý.
- `RentalOrderWriter` không phát hành order cho đơn `expired`; `CompleteInvoiceAsync` ghi ledger nhưng tự tạo `payments(type='refund', status='requested')` khi webhook về sau hạn.
- `ReserveInvoiceCheckoutAsync` và xác nhận tiền mặt chặn `expired`; mọi bộ lọc "đơn đang chiếm xe" thêm `expired` (availability, điều phối, danh sách xe, lịch tài xế).
- Migration `0030_rental_expiry.sql`: cột `expire_at`/`expired_at`, CHECK có `expired`, index theo `expire_at`, backfill, khai báo lại `fn_rental_is_blocking`.

## Kiểm chứng

- `dotnet build -warnaserror` sạch; `dotnet test`: **1144 đạt / 3 lỗi có sẵn từ trước** (2 test `VehicleBookingMigrationTests` do migration 0015 trùng tiền tố `0015_` trong `Single(...)`, 1 test `CustomerLicenseUploadTests` về quyền media Manager — đã xác nhận lỗi tồn tại trên bản gốc bằng cách stash thay đổi và chạy lại).
- 32 test FR-042 đạt: `RentalExpiryDomainTests` (13) và `RentalExpiryIntegrationTests` (19) trên PostgreSQL tạm, gồm cả canh tranh khoá, chạy lại job, backfill/migration replay.
- `NavigationCountsApiTests.Swagger_documents_snapshot_envelope_and_auth_errors` đôi lúc timeout khi chạy toàn bộ suite song song; chạy riêng 12/12 đạt và không liên quan thay đổi này.

## Giới hạn

- Job chạy trong tiến trình API (BackgroundService), không phải scheduler ngoài: nhiều instance cùng chạy sẽ cùng quét nhưng `UpdateToExpiredAsync` idempotent nên không xử lý hai lần.
- Thông báo email "sắp hết hạn/đã hết hạn" chưa gửi: `IOperationalNotifications` hiện không có mẫu cho tình huống này; cần bổ sung mẫu email trước khi bật.
- Mốc hạn tính từ thời điểm đặt (giả định đã ghi trong `docs/open-questions.md`), không phải `start_time - 1 giờ`.
- Chưa deploy; migration 0030 chỉ mới chạy trên database test cục bộ.
