# ADR-005 — Sổ cái điểm thưởng bất biến (Loyalty Ledger) và cơ chế khóa tranh chấp số dư
- Status: Accepted
- Ngày: 2026-10-14

## Bối cảnh
Chương trình khách hàng thân thiết (Loyalty Program, FR-029) của hệ thống cho thuê xe VRS (Thong Dong) cần quản lý điểm thưởng của khách hàng với độ chính xác và an toàn tài chính tuyệt đối:
1. **Rủi ro phân kỳ số dư (Balance Drift)**: Nếu chỉ lưu một cột số dư `customers.loyalty_point` và thực hiện các lệnh cộng trừ trực tiếp, khi xảy ra lỗi mạng, deadlock, hoặc gọi đồng thời, số dư sẽ bị sai lệch so với thực tế các sự kiện tích/tiêu điểm mà không có căn cứ đối soát.
2. **Rủi ro đua tranh và cộng đôi (Double-crediting)**: Khi một đơn thuê hoàn thành (`complete`), các sự kiện retry hoặc gọi đồng thời nhiều lần có thể làm khách hàng được tích điểm nhiều lần cho cùng một đơn thuê.
3. **Rủi ro số dư âm (Negative Balance)**: Khi thực hiện tiêu điểm (redeem) hoặc điều chỉnh giảm (adjust), số dư không bao giờ được phép âm (`balance < 0`).
4. **Phân kỳ giữa các pha phát triển**: Pha 1 chỉ triển khai Tích điểm (Earn) và Điều chỉnh (Adjustment). Tính năng Tiêu điểm giảm giá tiền thuê (Redemption) thuộc Pha 2 và chưa được phép can thiệp vào luồng thanh toán hiện hành của đơn thuê (BE-12/BE-18) nhưng phải chuẩn bị sẵn Domain logic và cổng kiểm soát.

## Quyết định
1. **Kiến trúc Sổ cái 2 tầng (Dual-layer Ledger)**:
   - **Tầng ghi nhận sự kiện (Source of Truth)**: Bảng `customer_loyalty_transactions` lưu vết từng giao dịch (`earn`, `redeem`, `reverse`, `adjust`) theo mô hình append-only.
     - Cột `balance_after` lưu số dư lũy kế ngay sau mỗi giao dịch.
     - Trigger bất biến (`trg_clt_immutable`) chặn hoàn toàn mọi thao tác `UPDATE`, `DELETE`, `TRUNCATE`.
   - **Tầng đệm hiệu năng (Cached Balance)**: Cột `customers.loyalty_point` chỉ đóng vai trò bộ đệm đọc nhanh (read-cache).
     - Trigger `BEFORE INSERT` (`trg_clt_before_insert`) khóa dòng khách hàng `FOR UPDATE`, kiểm tra số dư hiện tại và tính `NEW.balance_after`. Trigger này tuyệt đối KHÔNG ghi dữ liệu sang bảng khác để tránh cộng đôi khi xảy ra `ON CONFLICT DO NOTHING`.
     - Trigger `AFTER INSERT` (`trg_clt_after_insert`) đồng bộ số dư `customers.loyalty_point = NEW.balance_after`.

2. **Idempotency tuyệt đối bằng Partial Unique Indexes**:
   - Giao dịch tích điểm tự động theo đơn thuê: `ux_clt_unique_ref` trên `(type, ref_type, ref_id) WHERE ref_type IS NOT NULL AND type IN ('earn','redeem','reverse')`.
     Lệnh insert dùng `ON CONFLICT (type, ref_type, ref_id) WHERE ... DO NOTHING`. Dù lệnh `CompleteAsync` được retry 5 lần hay chạy song song, chỉ duy nhất 1 dòng ledger được ghi và không có điểm cộng đôi.
   - Giao dịch điều chỉnh thủ công bởi quản trị viên: `ux_clt_unique_idempotency` trên `(customer_id, idempotency_key) WHERE idempotency_key IS NOT NULL`.

3. **Cơ chế kiểm soát số dư không âm (Check Constraint & Locking)**:
   - Constraint `ck_clt_balance_after_nonneg CHECK (balance_after >= 0)` bắt buộc số dư sau mọi giao dịch không được âm.
   - Khi vi phạm, PostgreSQL trả về mã lỗi `23514` (check_violation), được Repository tầng Infrastructure ánh xạ thành `ConflictException("INSUFFICIENT_POINTS")` với mã HTTP 409 chuẩn RFC 7807.

4. **Kiểm toán và đối soát tự động (Reconciliation View)**:
   - View `v_loyalty_reconciliation` tính toán độc lập `ledger_sum = SUM(points)` từ bảng `customer_loyalty_transactions` và so sánh với `customers.loyalty_point`.
   - Endpoint `GET /api/admin/loyalty/reconciliation` cho phép quản trị viên tra cứu ngay lập tức các tài khoản có `drift <> 0`.

5. **Phân hạng thành viên theo tích lũy trọn đời (Lifetime Earned)**:
   - Hạng thành viên (`Member`, `Silver`, `Gold`, `Platinum`) được xác định độc lập dựa trên `lifetime_earned = SUM(points) WHERE type = 'earn'`.
   - Tiêu điểm (redeem) hoặc điều chỉnh kỹ thuật (adjust) không làm hạ hạng thành viên của khách hàng.
   - Hệ số nhân điểm thưởng áp dụng theo hạng: Member (1.0x), Silver (1.1x), Gold (1.25x), Platinum (1.5x).

6. **Đóng cổng Pha 2 an toàn (Feature Gating)**:
   - Cấu hình `Loyalty:Redemption:Enabled` mặc định là `false`.
   - Domain layer hiện thực đầy đủ `CalculateRedemptionQuote` và `ValidateRedemption`.
   - Khi gọi `GET /api/loyalty/redemption-quote`, Application layer ném `ConflictException("REDEMPTION_DISABLED")` (HTTP 409) rõ ràng, tuyệt đối không can thiệp vào mã nguồn tạo đơn hay thanh toán hiện hành.

## Hệ quả
1. Đảm bảo tính toán vẹn dữ liệu sổ cái tài chính, loại bỏ nguy cơ cộng đôi điểm thưởng do retry hoặc concurrency.
2. Dễ dàng đối soát sai lệch số dư thông qua view kiểm toán độc lập.
3. Sẵn sàng tích hợp mượt mà sang Pha 2 (Redemption) khi nghiệp vụ thanh toán được kích hoạt, không cần sửa đổi kiến trúc cơ sở dữ liệu.
