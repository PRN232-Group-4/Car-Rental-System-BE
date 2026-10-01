# 2026-10-01 — Invoice API, refund approval và subscription-plan delete
- **FR/NFR:** FR-019, FR-020, FR-021, FR-022
- **Loại:** feat

## Đã làm

- Thêm API hóa đơn: `GET /api/invoices`, `GET /api/invoices/{id}` (customer chỉ thấy hóa đơn của mình, Admin/Staff thấy tất cả) và `POST /api/rentals/{id}/invoice` cho direct rental, idempotent, chặn rental subscription bằng 400.
- Invoice amount giờ bao gồm cọc (`total_price + deposit_price`) để cọc thực sự được thu qua checkout.
- Thêm `GET /api/payments` (lọc theo `invoiceId`) đọc ledger payment.
- Hoàn cọc tự động sau post-inspection đạt: tạo payment `type='refund'`, `method='deposit'`, trạng thái `requested`, idempotent, chỉ khi invoice đã `paid`.
- Thêm luồng duyệt hoàn tiền: `POST /api/payments/refunds/{id}/approve` và `/reject` (Admin). Audit qua `payments.reviewed_by`/`reviewed_at`.
- Thêm `GET /api/subscription-plans/{id}` và `DELETE /api/subscription-plans/{id}` (Admin); delete là soft-delete khi plan đã có `customer_subscriptions`, xóa cứng khi chưa dùng.
- Migration `db/migrations/0004_payment_refund_review.sql` và cập nhật `docs/db/Init.sql` cho DB mới.

## Chưa làm

- Payout PayOS thực tế cho refund (hiện ghi ledger trạng thái `paid`).
- Integration test DB (NFR-007 chưa có test project).

## Test

- `dotnet build Car-Rental-System-BE.sln --no-restore` xanh, 0 warning/error.
- Chưa có integration test do repo chưa có test project.

## Quyết định/giả định

- Refund dùng chính bảng `payments` làm ledger; duyệt xong đánh dấu `paid` vì chưa có payout worker.
- Cọc nằm trong invoice amount (không tạo invoice riêng) để tận dụng checkout/webhook hiện có.
- DELETE plan ưu tiên soft-delete để không mồ côi `customer_subscriptions` (schema cố ý không FK).
- DB đang chạy cần apply `0004` thủ công; DB mới lấy cột mới từ `Init.sql`.
