# FR-042 — Hạn duyệt/thanh toán và tự động hết hạn yêu cầu đặt xe
- **Status:** Implemented (job + lifecycle + payment gate); **Priority:** P1; **Milestone:** M6

## Mô tả

Yêu cầu đặt xe (`rentals` ở trạng thái `pending`) chỉ được giữ lịch trong một cửa sổ hữu hạn. Trong cửa sổ đó nhân viên phải **duyệt** và khách phải **thanh toán**; nếu thiếu một trong hai, job nền chuyển yêu cầu sang trạng thái `expired` và giải phóng lịch xe.

| Điều kiện | Kết quả |
|---|---|
| `startTime < now + minimumLeadMinutes` | Từ chối tạo đơn (400 `RENTAL_START_TIME_TOO_SOON`) |
| Quá `expire_at` mà chưa duyệt hoặc chưa thanh toán | Job chuyển sang `expired` |
| Đã duyệt **và** (gói trả trước hoặc hóa đơn thuê đã `paid`) trước `expire_at` | Đơn giữ nguyên, tiếp tục luồng bình thường |
| Duyệt/thanh toán đơn đã `expired` | Bị chặn 409 (`ILLEGAL_TRANSITION` / `RENTAL_EXPIRED`) |

## Quy tắc

- `expire_at = thời điểm đặt + approvalWindowMinutes` (mặc định 60 phút), lưu ở `rentals.expire_at`. Đổi quy tắc (ví dụ mốc `start_time - 1 giờ`) chỉ cần đổi cách tính/backfill cột này, không cần đổi schema.
- Trạng thái `expired` là trạng thái kết thúc: không duyệt, không bắt đầu, không trả xe, không hoàn tất, không hủy lại.
- Đơn hết hạn **không** giữ lịch: `fn_rental_is_blocking('expired') = false`, trigger `trg_rentals_sync_booking` đặt `vehicle_bookings.is_blocking = false`, nên khung giờ đó lại tìm thấy xe.
- Hóa đơn thuê chưa thu tiền của đơn hết hạn bị hủy (`invoices.status='cancelled'`) trong cùng transaction.
- Khách kịp trả tiền nhưng webhook PayOS về sau khi job đã chạy: khoản thu vẫn được ghi vào sổ cái (bằng chứng nhận tiền), **không** phát hành `rental_orders`, và hệ thống tự tạo yêu cầu hoàn tiền `type='refund', status='requested'` để Admin xử lý theo FR-021.
- Điều kiện tranh chấp được kiểm tra lại trong transaction: `pg_advisory_xact_lock(rental_id)` + `SELECT ... FOR UPDATE`, cùng thứ tự khóa với duyệt (`RentalApprovalWriter`) và thanh toán (`NpgsqlPaymentRepository`), nên duyệt/thanh toán sát hạn không bị xử lý hai lần.

## Cấu hình

`Rental:Expiry` (`RentalExpiryOptions`):

| Khóa | Mặc định | Ý nghĩa |
|---|---|---|
| `MinimumLeadMinutes` | 60 | Giờ nhận xe phải cách thời điểm đặt tối thiểu bao nhiêu phút |
| `ApprovalWindowMinutes` | 60 | Cửa sổ duyệt + thanh toán tính từ lúc đặt |
| `ScanBatchSize` | 200 | Số yêu cầu tối đa mỗi lượt quét |
| `Enabled` | true | Tắt job khi cần bảo trì dữ liệu |

## API

- `POST /api/rentals/hourly`, `POST /api/rentals/longterm`: thêm ràng buộc `startTime >= now + MinimumLeadMinutes`, trả 400 `RENTAL_START_TIME_TOO_SOON`; đơn tạo ra có `expire_at`.
- `GET /api/rentals/mine`, `GET /api/rentals/mine/{id}`: thêm `expireAt: datetime|null`; tab mới `expired`.
- `GET /api/rentals` (vận hành): nhận `status=expired` như một trạng thái hợp lệ.
- `POST /api/rentals/{id}/confirm|start|return|complete`: đơn `expired` trả 409.
- `POST /api/invoices/{id}/checkout` (PayOS) và xác nhận thu tiền mặt: đơn `expired` trả 409 `RENTAL_EXPIRED` / `INVOICE_NOT_PAYABLE`.
- Invoice detail: `canPay=false`, `paymentBlockedReason=INVOICE_NOT_PAYABLE`.

## Acceptance

- [x] `startTime` dưới ngưỡng bị từ chối ở Application boundary, không ghi DB.
- [x] Job chạy mỗi phút, quét `pending`/`pre_inspection` quá `expire_at` và chưa đủ điều kiện duyệt + thanh toán.
- [x] Đơn hết hạn có `expired_at`, dòng lịch sử `to_status='expired'` với actor `System`, lý do tiếng Việt.
- [x] Hóa đơn chưa thu bị hủy; `vehicle_bookings.is_blocking=false`; xe lại tìm thấy cho khung giờ cũ.
- [x] Duyệt/thanh toán đơn hết hạn bị chặn; chạy job lần hai không xử lý lại.
- [x] Thanh toán muộn sau khi hết hạn: ghi ledger, không phát hành order, tự tạo yêu cầu hoàn tiền.
- [x] Thuê bằng gói chỉ cần duyệt; chưa duyệt thì vẫn hết hạn (không có hóa đơn).
- [x] Migration idempotent (cột `expire_at`/`expired_at`, CHECK có `expired`, index, backfill).
- [x] Test Domain (state machine, ngưỡng giờ, điều kiện hoàn tất, chặn thanh toán) và integration PostgreSQL.

## Giả định

- Cửa sổ hạn tính từ **thời điểm đặt** (`created_at`), không phải `start_time - 1 giờ`. Xem `docs/open-questions.md`.
- Backfill cho đơn đang mở trước khi triển khai dùng `GREATEST(created_at + 1 giờ, deploy + 1 giờ)` để việc bật tính năng không làm hết hạn hàng loạt đơn đang chờ.

## Thay đổi

- `changes/2026-10-23-rental-expiry.md`
- Migration `0030_rental_expiry.sql`
