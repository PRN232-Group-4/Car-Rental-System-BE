# Thay đổi: Sổ cái điểm thưởng & Tích điểm (FR-029 / BE-27 Phase 1)

- **Ngày:** 2026-10-14
- **Module:** BE-27 (FR-029: Chương trình khách hàng thân thiết — Pha 1: Tích điểm & Quản lý điểm thưởng)
- **Nhánh:** `feat/be-27-loyalty`
- **Tác giả:** Antigravity / Huy

## 1. Bối cảnh & Mục tiêu
Hiện thực Pha 1 module Loyalty theo đặc tả `specs/FR-029-loyalty.md`:
- Hệ thống sổ cái 2 tầng (dual-layer ledger): ghi nhận từng giao dịch điểm vào bảng bất biến `customer_loyalty_transactions` và đồng bộ số dư đệm `customers.loyalty_point` tự động qua database triggers.
- Idempotency tuyệt đối: chặn cộng đôi điểm thưởng khi retry hoặc thực hiện `complete` đồng thời nhiều lần.
- Tính điểm thưởng theo hạng thành viên (`Member`: 1.0x, `Silver`: 1.1x, `Gold`: 1.25x, `Platinum`: 1.5x) dựa trên tổng điểm tích lũy trọn đời (`lifetime_earned_points`).
- Căn cứ tích điểm là tiền thuê thuần trước thuế VAT (`net_rent_basis`), loại trừ VAT và tiền cọc; đơn thuê bao (`is_subscription = true`) không tích điểm.
- Hỗ trợ API tra cứu tóm tắt, lịch sử giao dịch phân trang, điều chỉnh điểm thủ công bởi quản trị viên, báo cáo đối soát phân kỳ số dư (`v_loyalty_reconciliation`).
- Chuẩn bị sẵn Domain/Application contract cho Pha 2 (Redemption) nhưng đóng cổng an toàn bằng cấu hình (`Redemption.Enabled = false`) và ném 409 `REDEMPTION_DISABLED`, không can thiệp vào luồng thanh toán hiện hành của BE-12/BE-18.

## 2. Các thay đổi chi tiết

### 2.1. Cơ sở dữ liệu (Migration `0018_loyalty.sql`)
- Tạo bảng sổ cái `customer_loyalty_transactions`: `id`, `customer_id`, `rental_id`, `type`, `points`, `reason`, `ref_type`, `ref_id`, `idempotency_key`, `balance_after`, `created_by`, `created_at`.
- Ràng buộc:
  - `ck_clt_type`: `earn`, `redeem`, `reverse`, `adjust`.
  - `ck_clt_points_rule`: `earn` > 0, `redeem` < 0, `adjust` <> 0.
  - `ck_clt_balance_after_nonneg`: `balance_after >= 0`.
- Triggers:
  - `trg_clt_before_insert`: khóa dòng khách hàng `FOR UPDATE`, kiểm tra số dư hiện tại và tính `NEW.balance_after`.
  - `trg_clt_after_insert`: cập nhật `customers.loyalty_point = NEW.balance_after`.
  - `trg_clt_immutable`: chặn hoàn toàn mọi lệnh `UPDATE`, `DELETE`, `TRUNCATE`.
- Partial Unique Indexes:
  - `ux_clt_unique_ref`: chặn trùng lặp giao dịch theo tham chiếu `(type, ref_type, ref_id)`.
  - `ux_clt_unique_idempotency`: chặn trùng lặp điều chỉnh theo `(customer_id, idempotency_key)`.
- View `v_loyalty_reconciliation`: đối soát tự động giữa số dư đệm và tổng điểm ledger `drift = cached - ledger_sum`.
- Nạp số dư đầu kỳ: nạp idempotent số dư hiện có trong `customers.loyalty_point` thành các giao dịch khởi tạo ban đầu.

### 2.2. Tầng Domain (`VehicleRental.Domain/Loyalty`)
- `LoyaltyTransactionType`: enum các loại giao dịch và mapping DB strings.
- `LoyaltyTierConfig`, `LoyaltyCustomerSummary`, `LoyaltyEarnResult`, `RedemptionQuote`.
- `LoyaltyDomainRules`:
  - `ResolveTier` & `ResolveNextTier`: phân hạng theo ngưỡng tích lũy trọn đời.
  - `CalculateEarn`: tính điểm cơ sở và điểm thực nhận theo hệ số nhân hạng.
  - `CalculateRedemptionQuote`: tính hạn mức điểm quy đổi tối đa (theo trần % và bước điểm quy đổi).
  - `ValidateRedemption`: thẩm định tính hợp lệ của yêu cầu tiêu điểm.
  - `ValidateConfiguration`: kiểm tra tính toàn vẹn cấu hình tier và redemption.

### 2.3. Tầng Application (`VehicleRental.Application/Loyalty`)
- `LoyaltyOptions`: cấu hình `VndPerPoint`, danh sách `Tiers`, và `RedemptionOptions` (`Enabled = false`).
- `LoyaltyContracts`: DTOs (`LoyaltySummaryResponse`, `LoyaltyTransactionDto`, `PagedLoyaltyTransactionsResponse`, `RedemptionQuoteResponse`, `LoyaltyReconciliationDto`), `ILoyaltyRepository`, `ILoyaltyService`, `ILoyaltyRedemption`.
- `LoyaltyService`: điều phối lấy tóm tắt, lịch sử giao dịch phân trang, điều chỉnh điểm thủ công (xử lý idempotency key), lấy báo giá quy đổi (ném 409 khi disabled), và xem báo cáo đối soát.

### 2.4. Tầng Infrastructure (`VehicleRental.Infrastructure/Loyalty` & `Lifecycle`)
- `NpgsqlLoyaltyRepository`: thực thi các thao tác đọc/ghi sổ cái trên PostgreSQL, ánh xạ mã lỗi 23514 sang `ConflictException("INSUFFICIENT_POINTS")`, xử lý `AwardForRentalAsync` với `ON CONFLICT DO NOTHING`.
- `LoyaltyAwardAdapter`: gọi `ILoyaltyRepository` và `LoyaltyDomainRules`, loại bỏ hoàn toàn việc can thiệp trực tiếp vào `customers.loyalty_point` từ tầng code.

### 2.5. Tầng WebApi (`VehicleRental.WebApi`)
- `LoyaltyController`:
  - `GET /api/loyalty/me` (Customer)
  - `GET /api/loyalty/me/transactions` (Customer)
  - `GET /api/customers/{id}/loyalty` (Admin, Manager)
  - `POST /api/customers/{id}/loyalty/adjustments` (Admin)
  - `GET /api/loyalty/redemption-quote` (Customer - trả 409 trong Pha 1)
  - `GET /api/admin/loyalty/reconciliation` (Admin)
- `ApiSuccessResponseFilter`: bổ sung các message mappings chuẩn hóa cho toàn bộ actions của Loyalty.
- `Program.cs`: đăng ký DI `ValidateOnStart` cho `LoyaltyOptions`, repo, services và adapters.

## 3. Kiểm chứng & Kết quả kiểm thử
- `dotnet build -warnaserror`: Xanh 100%, 0 warning, 0 error.
- Unit & Schema tests:
  - `LoyaltySchemaTests`: 6/6 pass (triggers, immutability, balance check, idempotency).
  - `LoyaltyDomainTests`: 23/23 pass (ngưỡng hạng, hệ số nhân, tính điểm, quote quy đổi, validation).
  - `LoyaltyServiceTests`: 7/7 pass (nghiệp vụ service, idempotency key, feature gating).
  - `LoyaltyControllerTests`: 7/7 pass (HTTP status, phân quyền, NotFound, Conflict).
- Integration tests:
  - `LoyaltyIntegrationTests`: 5/5 pass (tích điểm khi complete, 2 complete song song chỉ ghi 1 dòng, 200 thao tác ngẫu nhiên có zero drift, đơn subscription không tích điểm, kiểm tra số dư âm).
- Toàn bộ suite Loyalty: **48/48 pass** trên database Neon PostgreSQL.
