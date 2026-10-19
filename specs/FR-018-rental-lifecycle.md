# FR-018 — Vòng đời đơn thuê: hủy, hoàn tất, quá giờ, quyết toán (BE-17)

## 2026-10-18 — Staff xác nhận trước thanh toán (người dùng xác nhận)

- Đơn mới ở pending; staff theo policy CustomerService xác nhận trước, ghi confirmed_at và chuyển pre_inspection mà không yêu cầu invoice đã paid.
- Checkout hóa đơn rental chỉ mở sau xác nhận thực sự (confirmed_at có giá trị, không pending/cancelled); kiểm tra trong transaction khóa rental/invoice trước khi ghi checkout marker/gọi PayOS.
- Hóa đơn bổ sung overtime giữ quy tắc thu sau quyết toán. Không mở checkout cho đơn cancelled hoặc invoice không unpaid.
- Start/bàn giao đơn trực tiếp vẫn yêu cầu invoice rental paid; subscription giữ quy tắc hiện tại. Legacy pre_inspection chưa có confirmed_at cần staff gọi confirm để xác nhận bổ sung, không tự suy diễn duyệt từ kiểm định.

- **Status:** In progress; **Priority:** P0; **Milestone:** M4; **Assignee:** Huy
- **Phụ thuộc:** BE-14 (assignment), BE-15 (pre-inspection, Lạc), BE-16 (post-inspection, Anh), BE-18/19 (invoice/payment, Phúc), BE-22 (subscription, Phúc). **Phục vụ:** FE-07 (Huy), FE-15/16 (Trí), BE-27 (loyalty), BE-26 (email), BE-28 (dashboard).
- **Migration dự kiến:** `0010_rental_lifecycle.sql`.

## 1. Hiện trạng và khoảng trống

Đã có: `POST/PATCH …/status` (Admin/Staff) chuyển trạng thái tuần tự; `rentals.status` là `VARCHAR(255)` không CHECK; không có hủy do khách; không có `returned_at` (không biết giờ trả thực); chưa tính `overtime_price`; `AddInspectionAsync` hiện **tự tạo bản ghi hoàn cọc ngay khi post-inspection đạt** (trước quyết toán quá giờ → sai tiền) và tự hủy khi pre-inspection hỏng; không có audit; không có API đọc đơn cho khách.

## 2. Quyết định đã chốt

| # | Quyết định | Nguồn |
|---|---|---|
| D1 | Quá giờ → **tự động trừ vào tiền cọc**; phần thiếu **phát sinh hóa đơn bổ sung** | Huy |
| D2 | Điều kiện khách tự hủy: **chỉ khi `pending`**; trước giờ nhận >= 24h hoàn 100%; trong 24h giữ 20% (tiền thuê + VAT); từ giờ nhận khách không tự hủy | Claude chọn, cần xác nhận (O1) |
| D3 | Lệnh chuyển trạng thái là **các lệnh riêng** (`confirm`, `start`, `return`, `complete`, `cancel`), `PATCH status` cũ trở thành bí danh deprecated qua cùng state machine | Thiết kế |
| D4 | Quyết toán (quá giờ + hoàn cọc) thực hiện **một lần tại `completed`**; hành vi "tự hoàn cọc khi post-inspection đạt" ở BE-16 phải chuyển sang đây | Thiết kế (cần Anh/Lạc đồng ý, O2) |
| D5 | Hóa đơn bổ sung dùng cột `invoices.kind` (`rental`/`overtime`) để không phá tính idempotent của FR-019 (cần Phúc đồng ý, O3) | Thiết kế |
| D6 | Dung sai trả xe 15 phút; vượt dung sai tính theo số giờ **bắt đầu** | Claude chọn (O4) |

## 3. State machine (Domain, thuần)

```text
pending ──confirm──▶ pre_inspection ──start──▶ renting ──return──▶ post_inspection ──complete──▶ completed
   │                       │
   └────────cancel─────────┴──────────────cancel──────▶ cancelled
```

| Lệnh | Từ → Đến | Ai được | Điều kiện (409 nếu vi phạm) |
|---|---|---|---|
| `confirm` | pending → pre_inspection | Policy CustomerService | Duyệt yêu cầu, ghi confirmed_at trước thanh toán; legacy pre_inspection chưa có timestamp được duyệt bổ sung. Đơn theo gói trả trước phát hành order; trực tiếp chờ paid (FR-036) |
| `start` | pre_inspection → renting | Policy CustomerService | Đơn trực tiếp cần hóa đơn rental paid; có pre-inspection `is_ok = true`; có bản ghi `assigned` (có tài xế nếu `has_driver`); tự lái: GPLX khách `approved` và còn hạn; ghi `started_at`; xe → `renting`, `is_renting = true` |
| `return` | renting → post_inspection | Admin, Manager, Staff | Ghi `returned_at` (mặc định `now`; staff có thể truyền `returnedAt` trong `[started_at, now]`) |
| `complete` | post_inspection → completed | Admin, Manager, Staff | Có post-inspection; chạy **quyết toán** (mục 5) trong cùng transaction; ghi `completed_at` |
| `cancel` | pending/pre_inspection → cancelled | Customer (chủ đơn, chỉ `pending`), Admin, Manager, Staff | Quy tắc hủy mục 4 |

Quy tắc chung: mọi lệnh khóa dòng `rentals` (`SELECT … FOR UPDATE`), kiểm tra trạng thái hiện tại **trong** transaction, ghi `rental_status_history`, cập nhật `status_changed_at`. Gọi lại lệnh khi đơn **đã ở trạng thái đích** → `409 ALREADY_IN_STATE` kèm `currentStatus` (client coi như thành công), mọi trường hợp sai khác → `409 ILLEGAL_TRANSITION` kèm `from`, `to`, `allowedNext`. Thay đổi trạng thái kích hoạt cập nhật `vehicle_bookings.is_blocking` qua trigger của BE-10; `completed`/`cancelled` giải phóng xe; xe về `available` trừ khi đang `maintenance` hoặc còn phiếu bảo dưỡng chặn.

Tự hủy do pre-inspection không đạt (BE-15): vẫn đi qua `cancel` với `initiator = system`, lý do `PRE_INSPECTION_FAILED`, **không phí**.

## 4. Hủy đơn

Cấu hình (`Rental:Cancellation`, `ValidateOnStart`): `SelfCancelStatuses=["pending"]`, `FreeHoursBeforeStart=24`, `LateFeePercent=20`.

Với đơn trực tiếp đã thanh toán, gọi `T = start_time − now`:

| Người hủy | Điều kiện | Phí giữ lại | Hoàn |
|---|---|---|---|
| Khách (chỉ `pending`) | `T >= 24h` | 0 | 100% số đã trả |
| Khách (chỉ `pending`) | `0 < T < 24h` | 20% × (tiền thuê + VAT) | Số đã trả − phí (gồm cọc) |
| Khách | `T <= 0` | — | Không cho tự hủy: 409 `SELF_CANCEL_NOT_ALLOWED` |
| Staff — `initiator=branch` | pending/pre_inspection | 0 | 100% |
| Staff — `initiator=customer_request` | pending/pre_inspection | Như quy tắc khách theo `T` (staff có thể `waiveFee=true`, ghi lý do) | Tương ứng |
| Hệ thống | pre-inspection không đạt | 0 | 100% |

* Đơn chưa thanh toán: hóa đơn rental được **hủy** qua `IInvoiceGateway.VoidIfUnpaidAsync` (không phát sinh hoàn tiền).
* Đơn theo gói: không phát sinh tiền (số giờ chỉ bị trừ khi `completed`).
* Hoàn tiền: tạo **một** bản ghi `payments` loại `refund`, trạng thái `requested` (phê duyệt theo FR-021), idempotent theo đơn.
* Ghi `cancelled_at`, `cancelled_by` (`customer|staff|system`), `cancel_reason`.
* Xem trước: `GET /api/rentals/{id}/cancellation-preview` → `{ allowed, feeAmount, refundAmount, reasons[], freeUntil }`.

## 5. Quyết toán tại `complete`

Thực hiện trong **một transaction**, khóa dòng `rentals`, đọc `rental_policies` theo `rentals.policy_id` (policy bất biến nên đọc an toàn).

1. `scheduledEnd` = `end_time` của chi tiết giờ/hợp đồng; `returnedAt` đã ghi ở `return`.
2. `lateness = returnedAt − scheduledEnd`. Nếu `lateness <= 15 phút` → `overtimeHours = 0`; ngược lại `overtimeHours = ceil(lateness / 60 phút)`.
3. Đơn giá giờ: thuê theo giờ = `hourly_price_snapshot`; dài hạn = `monthly_price_snapshot / 720`.
4. `overtime_price = overtimeHours × đơn giá × (1 + overtime_fee_rate/100)`; `overtimeVat = overtime_price × vat_rate/100`; `overtimeTotal = overtime_price + overtimeVat`. (Cách hiểu `overtime_fee_rate` là **phụ phí cộng thêm** trên giá giờ — DTO giới hạn 0–100 — xem O5.)
5. `depositHeld` = `deposit_price` nếu đơn trực tiếp đã thanh toán đủ hóa đơn (hoặc đơn theo gói có khoản cọc đã thu — đọc qua `IRentalPaymentGate.GetDepositHeldAsync`; xác nhận giá trị `payments.type` thật của Phúc), ngược lại 0.
6. `deducted = min(overtimeTotal, depositHeld)`; `shortfall = overtimeTotal − deducted`.
7. `shortfall > 0` và đơn trực tiếp → tạo **hóa đơn bổ sung** (`kind='overtime'`, `invoice_number = INV-yyyyMMdd-{rentalId}-OT`, `amount = shortfall`, `vat_amount` theo tỷ lệ, `due_at = now + 72h`). Đơn theo gói: giờ quá hạn trừ vào `remaining_hours` qua `ISubscriptionUsage` (cổng của BE-22; **chưa có thì chỉ ghi nhận `overtime_hours` và để lại việc trừ cho BE-22**), phần không trừ được tính tiền như trên nhưng ghi nhận bằng `payments` với `is_subscription_payment = true` (không hóa đơn).
8. `refund = depositHeld − deducted`; nếu `refund > 0` tạo **một** `payments` `refund` trạng thái `requested` (idempotent: đã có refund cho đơn thì bỏ qua).
9. Cập nhật `rentals`: `overtime_price`, `vat_price += overtimeVat`, `total_price += overtimeTotal`, `overtime_hours`, `returned_at`, `completed_at`; ghi `rental_settlements` (mục 6).
10. Gọi các **hook trong transaction** theo thứ tự cố định: (a) `ISubscriptionUsage` (BE-22: trừ giờ), (b) `ILoyaltyAward` (BE-27: cộng điểm, idempotent). Hook lỗi → rollback toàn bộ `complete`.
11. Sau `COMMIT`: phát `RentalStatusChanged` qua `IRentalEventSink` (mặc định ghi log; BE-26 cắm email/outbox).

Không còn tự tạo hoàn cọc ở `AddInspectionAsync` — **chuyển sang bước 8** (O2). Post-inspection `is_ok = false` vẫn tạo phiếu bảo dưỡng như hiện tại; cho phép `complete` (đơn đã trả) nhưng ghi `damage_review_required` vào `rental_settlements` để staff xử lý phí hư hại thủ công ngoài phạm vi (O6).

Xe trả trễ so với lượt kế tiếp (booking tiếp theo đã chặn): **không** kéo dài booking (sẽ vi phạm exclusion); `GET /api/rentals/overdue` liệt kê đơn `renting` quá `end_time` kèm đơn kế tiếp bị ảnh hưởng để staff xử lý.

## 6. Dữ liệu & migration `0010`

Tham chiếu (phải chạy thử trên PostgreSQL 16 + integration test; file LF):

```sql
DO $$
DECLARE bad INTEGER;
BEGIN
  SELECT count(*) INTO bad FROM rentals
   WHERE status NOT IN ('pending','pre_inspection','renting','post_inspection','completed','cancelled');
  IF bad > 0 THEN RAISE EXCEPTION 'rentals.status có % dòng ngoài tập hợp lệ', bad; END IF;
END $$;
ALTER TABLE rentals ADD CONSTRAINT ck_rentals_status
  CHECK (status IN ('pending','pre_inspection','renting','post_inspection','completed','cancelled'));

ALTER TABLE rentals
  ADD COLUMN IF NOT EXISTS confirmed_at       TIMESTAMP,
  ADD COLUMN IF NOT EXISTS started_at         TIMESTAMP,
  ADD COLUMN IF NOT EXISTS returned_at        TIMESTAMP,
  ADD COLUMN IF NOT EXISTS completed_at       TIMESTAMP,
  ADD COLUMN IF NOT EXISTS cancelled_at       TIMESTAMP,
  ADD COLUMN IF NOT EXISTS cancelled_by       VARCHAR(20),
  ADD COLUMN IF NOT EXISTS cancel_reason      VARCHAR(500),
  ADD COLUMN IF NOT EXISTS overtime_hours     INTEGER NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS status_changed_at  TIMESTAMP NOT NULL DEFAULT now();
ALTER TABLE rentals ADD CONSTRAINT ck_rentals_cancelled_by
  CHECK (cancelled_by IS NULL OR cancelled_by IN ('customer','staff','system'));

CREATE TABLE IF NOT EXISTS rental_status_history (
  id          BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  rental_id   INTEGER NOT NULL,
  from_status VARCHAR(20),
  to_status   VARCHAR(20) NOT NULL,
  actor_user_id INTEGER,
  actor_email VARCHAR(255),
  actor_role  VARCHAR(30),
  reason      VARCHAR(500),
  metadata    JSONB,
  created_at  TIMESTAMP NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_rsh_rental ON rental_status_history (rental_id, id);

CREATE OR REPLACE FUNCTION fn_immutable_row() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION '% là bảng chỉ ghi thêm (immutable)', TG_TABLE_NAME; END $$;
CREATE TRIGGER trg_rsh_immutable BEFORE UPDATE OR DELETE ON rental_status_history
  FOR EACH ROW EXECUTE FUNCTION fn_immutable_row();

CREATE TABLE IF NOT EXISTS rental_settlements (
  rental_id            INTEGER PRIMARY KEY,
  scheduled_end        TIMESTAMP NOT NULL,
  returned_at          TIMESTAMP NOT NULL,
  overtime_minutes     INTEGER NOT NULL DEFAULT 0,
  overtime_hours       INTEGER NOT NULL DEFAULT 0,
  overtime_amount      NUMERIC(14,2) NOT NULL DEFAULT 0,
  overtime_vat         NUMERIC(14,2) NOT NULL DEFAULT 0,
  deposit_held         NUMERIC(14,2) NOT NULL DEFAULT 0,
  deposit_deducted     NUMERIC(14,2) NOT NULL DEFAULT 0,
  shortfall_invoice_id INTEGER,
  refund_payment_id    INTEGER,
  damage_review_required BOOLEAN NOT NULL DEFAULT FALSE,
  created_at           TIMESTAMP NOT NULL DEFAULT now()
);

-- Chỉ khi đã thống nhất với Phúc (O3):
ALTER TABLE invoices ADD COLUMN IF NOT EXISTS kind VARCHAR(20) NOT NULL DEFAULT 'rental';
ALTER TABLE invoices ADD CONSTRAINT ck_invoices_kind CHECK (kind IN ('rental','overtime'));
CREATE UNIQUE INDEX IF NOT EXISTS ux_invoices_rental_kind ON invoices (rental_id) WHERE kind = 'rental';
```

Backfill: `status_changed_at = created_at` cho dữ liệu cũ; không dựng lịch sử giả. Trigger `fn_check_payment_matches_rental`/`trg_invoice_no_subscription` giữ nguyên.

## 7. Hợp đồng API

Controller mới `RentalLifecycleController` và `RentalQueriesController` (đọc). Mọi action có Swagger + ánh xạ message.

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| POST | `/api/rentals/{id}/confirm` | Admin, Manager, Staff | pending → pre_inspection |
| POST | `/api/rentals/{id}/start` | Admin, Manager, Staff | pre_inspection → renting |
| POST | `/api/rentals/{id}/return` | Admin, Manager, Staff | renting → post_inspection; body `{ "returnedAt"?: UTC }` |
| POST | `/api/rentals/{id}/complete` | Admin, Manager, Staff | post_inspection → completed + quyết toán |
| POST | `/api/rentals/{id}/cancel` | Customer (chủ đơn), Admin, Manager, Staff | body `{ "reason", "initiator"?, "waiveFee"? }` |
| GET | `/api/rentals/{id}/cancellation-preview` | Customer (chủ đơn), staff | Xem phí/hoàn nếu hủy ngay |
| GET | `/api/rentals/mine?tab=&page=&pageSize=` | Customer | Đơn của tôi; `tab`: `upcoming` (pending, pre_inspection) · `ongoing` (renting) · `completed` (post_inspection, completed) · `cancelled` |
| GET | `/api/rentals/mine/{id}` | Customer (chủ đơn) | Chi tiết đầy đủ cho FE-07 |
| GET | `/api/rentals?status=&customerId=&vehicleId=&from=&to=&page=` | Admin, Manager, Staff | Danh sách vận hành |
| GET | `/api/rentals/{id}/detail` | Admin, Manager, Staff | Chi tiết + timeline + settlement |
| GET | `/api/rentals/overdue` | Admin, Manager, Staff | Đơn `renting` quá giờ + đơn kế tiếp bị ảnh hưởng |
| PATCH/POST | `…/status` (cũ) | Admin, Staff | **Deprecated**: ánh xạ sang lệnh tương ứng, trả `Deprecation: true` |

`GET /api/rentals/mine/{id}` trả: `{ id, status, rentalType, isSubscription, vehicle {id, brand, model, mainImageUrl, color}, facility, start, end, hasDriver, pickup {method, location}, driver {fullName, avatarUrl, rating}|null, price {rent, vat, deposit, overtime, total, currency:"VND"}, invoice {id, number, status, amount}|null, inspections[{type, isOk, odometerKm, fuelBatteryLevel, damageNotes, imgUrls[], inspectedAt}], settlement {…}|null, timeline[{to, at, actor: "customer"|"staff"|"system", reason}], canCancel, cancellation {…preview…}, nextAction }`. Không lộ email/điện thoại nhân viên; tên tài xế + ảnh + điểm đánh giá được phép. Quyền chủ đơn: so khớp `rentals.customer_id` với hồ sơ khách của `ClaimTypes.NameIdentifier` (tái dùng dịch vụ xác định khách hiện hành đã có ở module profile; nếu chưa, tạo `ICurrentCustomer`).

Mã lỗi: `ILLEGAL_TRANSITION`, `ALREADY_IN_STATE`, `PAYMENT_REQUIRED`, `PRE_INSPECTION_REQUIRED`, `ASSIGNMENT_REQUIRED`, `CUSTOMER_LICENSE_NOT_VALID`, `POST_INSPECTION_REQUIRED`, `SELF_CANCEL_NOT_ALLOWED`, `RENTAL_NOT_FOUND`, `NOT_RENTAL_OWNER` (trả 404 thay vì 403 cho khách để không lộ sự tồn tại).

## 8. Kiến trúc & mã nguồn

* **Domain:** `RentalStatus` (enum + ánh xạ chuỗi), `RentalTrigger`, `RentalStateMachine.Evaluate(current, trigger)` → `Transition | Rejection(allowedNext)`; `OvertimeCalculator` (nhận `scheduledEnd, returnedAt, unitHourly, overtimeFeeRate, vatRate, grace` → kết quả; làm tròn tiền `decimal` theo VND không lẻ: `MidpointRounding.AwayFromZero` về số nguyên); `CancellationPolicy.Evaluate(now, start, paidAmounts, actor, options)`; `SettlementCalculator` (deducted/shortfall/refund). **Tất cả thuần, không I/O** để đạt coverage cao.
* **Application:** `IRentalLifecycleService`, `IRentalQueries`, ports: `IRentalPaymentGate`, `IInvoiceGateway`, `ISubscriptionUsage`, `ILoyaltyAward`, `IRentalEventSink`, `IRentalTransitionHook`; `RentalOptions`/`CancellationOptions`.
* **Infrastructure:** `NpgsqlRentalLifecycleRepository` (transaction, `FOR UPDATE`, lịch sử), `NpgsqlRentalQueries`, các adapter cổng gọi repository của Phúc (không truy cập bảng của họ bằng SQL song song nếu service đã có).
* **WebApi:** hai controller; Swagger; message mapping. Tách rõ phần việc của BE-12 (tạo đơn/báo giá) và BE-17 (vòng đời + đọc): ghi vào README phân vai.
* Dùng `TimeProvider`; mọi mốc giờ UTC. Tiền: `decimal`, làm tròn tại một chỗ duy nhất (`MoneyRounding`).

## 9. Kiểm thử (tối thiểu)

* **Domain (unit):** toàn bộ ma trận 6 trạng thái × 5 lệnh (bảng kỳ vọng); `OvertimeCalculator` ở biên dung sai (14:59, 15:00, 15:01 phút, 60, 61 phút), hourly vs longterm, `overtime_fee_rate` 0/50/100, làm tròn VND; `CancellationPolicy` ở biên 24h (±1 giây), mọi người hủy; `SettlementCalculator` (đủ cọc, thiếu cọc, không quá giờ).
* **Integration:** từng lệnh thành công + từng điều kiện chặn; hai request `complete` song song → quyết toán đúng **một** lần (một refund, một cộng điểm); `ALREADY_IN_STATE` khi lặp; hủy hoàn tiền đúng số theo từng bậc; hóa đơn bổ sung đúng số và không phá `GET invoice` của FR-019; trigger `vehicle_bookings` giải phóng xe khi `completed/cancelled`; xe về `available`/giữ `maintenance`; `rental_status_history` đủ và **immutable** (UPDATE/DELETE bị chặn); migration chạy hai lần không lỗi; quyền chủ đơn (khách A không xem/hủy được đơn khách B, trả 404).
* **API:** phân quyền theo bảng; validation; Swagger; message mapping; alias deprecated.

## 10. Acceptance criteria

Bổ sung 2026-10-16: [sửa đơn trước thuê](FR-018-rental-editing.md), [danh sách và xác nhận bàn giao](FR-018-rental-handover.md), [pagination và lọc danh sách](FR-018-rental-query-pagination.md). Quyền staff vận hành trong các API hiện hành là policy CustomerService gồm Admin/Manager/CustomerService. Luồng mới dùng confirm → inspection → assignment → handover confirm; kiểm định sau confirm/return được hỗ trợ. Kết quả kiểm chứng được ghi trong changes/2026-10-16-rental-editing-handover.md.

- [ ] State machine ở Domain, bảng chuyển trạng thái được test đầy đủ; chuyển sai bị từ chối kèm `allowedNext`
- [ ] `ck_rentals_status`; `rental_status_history` bất biến, đủ actor/lý do
- [ ] Khách tự hủy theo D2; staff hủy có lý do; xem trước phí hoạt động
- [ ] `returned_at` được ghi; quá giờ tính đúng (dung sai 15 phút, làm tròn giờ bắt đầu)
- [ ] Quyết toán tại `complete`: trừ cọc, hóa đơn bổ sung khi thiếu, hoàn phần dư (một lần, idempotent)
- [ ] Hành vi auto-refund ở `AddInspectionAsync` đã được gỡ/chuyển (đã thống nhất với Anh/Lạc)
- [ ] Hook subscription + loyalty chạy trong transaction; lỗi hook rollback
- [ ] API đọc cho khách (`mine`, `mine/{id}`) đầy đủ cho FE-07, không rò dữ liệu nhạy cảm
- [ ] Alias `status` cũ hoạt động + `Deprecation`
- [ ] Tài liệu: ADR "Quyết toán tại completed + chính sách hủy", open-questions (O1–O6), `docs/progress.md`, `changes/…-rental-lifecycle.md`, CHANGELOG, `docs/business-rules.md`

## 11. Câu hỏi mở / giả định

* **O1.** Con số hủy (24h, 20%) do Claude đề xuất; cấu hình được, cần chủ nghiệp vụ xác nhận.
* **O2.** Gỡ auto-refund khỏi `AddInspectionAsync` (BE-16, Anh) và pre-inspection hỏng → gọi `cancel(system)`; cần Anh/Lạc đồng ý.
* **O3.** `invoices.kind` và lọc `kind='rental'` trong mã hóa đơn hiện có của Phúc; nếu Phúc không đồng ý, phương án thay thế phải được bàn trước khi code.
* **O4.** Dung sai 15 phút, tính giờ bắt đầu.
* **O5.** Công thức `overtime_fee_rate` (phụ phí cộng thêm trên giá giờ) và quy đổi dài hạn `monthly/720`.
* **O6.** Phí hư hại thu thêm: ngoài phạm vi, chỉ gắn cờ `damage_review_required`.
* **O7.** Khách có hóa đơn quá hạn bổ sung có bị chặn đặt đơn mới? Ngoài phạm vi, ghi nhận.

---

## Prompt thực thi (Claude Code — repo BE)

```text
Bạn là senior .NET/PostgreSQL engineer. Hiện thực BE-17 (FR-018) theo ĐÚNG specs/FR-018-rental-lifecycle.md (đã cập nhật). Điều kiện tiên quyết: BE-10 và BE-14 đã merge. Nhánh feat/be-17-rental-lifecycle từ main mới nhất; kiểm tra số migration.

TRƯỚC KHI CODE (bắt buộc): đọc agent.md, docs/*, specs/FR-012, FR-015..FR-021, FR-024, FR-029; toàn bộ OperationsController và NpgsqlOperationsRepository (CreateRentalAsync, ChangeRentalStatusAsync, AddInspectionAsync); module invoice/payment của Phúc (NpgsqlPaymentRepository, controller, giá trị payments.type và invoices.status THẬT); module subscription (BE-20/21/22) nếu có; tests hiện có. Sau đó viết ra (không sửa code) một bản "Điểm tích hợp cần xác nhận" gồm: giá trị status/type thực tế, nơi nào gọi ChangeRentalStatusAsync, auto-refund ở AddInspectionAsync, các mâu thuẫn với spec. DỪNG và đưa bản này cho tôi nếu có mâu thuẫn về O2/O3; chỉ tiếp tục khi không có.

QUY TẮC: như agent.md. Domain thuần (không I/O). Dùng TimeProvider. Tiền là decimal, làm tròn VND tại MỘT chỗ. Mọi lệnh khóa FOR UPDATE và kiểm tra trạng thái trong transaction. Không phá hợp đồng cũ (alias status deprecated). Không sửa trực tiếp bảng/logic của Phúc ngoài những điểm đã thống nhất trong O3.

THỨ TỰ: (1) migration 0010 + test schema (immutable history, CHECK, kind); (2) Domain + unit test đầy đủ ma trận; (3) Application (service, ports, options); (4) Infrastructure (repo, adapter cổng); (5) WebApi (2 controller, DTO, Swagger, message mapping, alias); (6) gỡ/chuyển auto-refund theo O2 (đã đồng ý); (7) integration + API test; (8) tài liệu.

KIỂM CHỨNG và dán kết quả thật: dotnet build -warnaserror; dotnet test (kể cả test đua tranh complete x2 chạy 20 lần); migration hai lần; kịch bản end-to-end bằng test: tạo đơn → confirm → pre-inspection → assign → start → return trễ 90 phút → complete → kiểm tra số tiền cọc trừ/hóa đơn bổ sung/hoàn/điểm.

BÁO CÁO: file đổi, kết quả lệnh, giả định, các điểm cần Phúc/Anh/Lạc xác nhận. Gặp mâu thuẫn: dừng và hỏi.
```
