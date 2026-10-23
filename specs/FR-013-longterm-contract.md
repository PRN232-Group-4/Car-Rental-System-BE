# FR-013 — Thuê dài hạn/hợp đồng

## Signed document upload — 2026-10-23
Customer owner, Admin, Manager or CustomerService uploads a signed PDF/JPEG/PNG/WebP document (non-empty, maximum 10 MiB) using multipart file and signedAt before handover. Validate declared MIME, extension and file signature; keep signedAt non-future. Store one private document per rental in PostgreSQL; authenticated owner/operations download only, attachment and no-store. Allow atomic replacement while pending/pre_inspection and freeze after start/cancellation. Long-term handover requires persisted upload evidence under the rental transaction lock; legacy URL metadata alone does not satisfy it. Creation and hourly rentals do not require a document. Migration 0031 adds private bytea evidence; do not backfill old URLs as verified uploads. Client confirmation cannot prove signature authenticity; staff review remains responsible for checking the signed document.

2026-10-22: Theo yêu cầu mới, lưu `hasDriver` cho cả hợp đồng dài hạn; không ép lựa chọn có tài xế về tự lái. Điều kiện tài xế/phân công/kiểm tra áp dụng cùng luồng theo giờ.
- **Status:** In progress (creation, interval locking and signed evidence implemented); **Priority:** P1; **Milestone:** M4
Tạo/renew/end contract tự lái. Acceptance: lock vehicle interval, contract link/signed date, payment schedule quyết định sau.

## BE-13 API — 2026-10-05
Create and interval locking exist. Add PUT /api/rentals/{id}/contract for Admin/Manager/CustomerService to record an HTTP(S) contract link (max 255 characters) and a non-future signedAt. Only pending/pre_inspection long-term rentals may be updated. Signed evidence is immutable; repeating identical values is idempotent. GET rental returns contract metadata and unit price snapshots under existing resource authorization. No document is fetched by the server. Renewal and payment schedules remain outside this change.
