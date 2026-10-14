# FR-013 — Thuê dài hạn/hợp đồng
- **Status:** In progress (creation, interval locking and signed evidence implemented); **Priority:** P1; **Milestone:** M4
Tạo/renew/end contract tự lái. Acceptance: lock vehicle interval, contract link/signed date, payment schedule quyết định sau.

## BE-13 API — 2026-10-05
Create and interval locking exist. Add PUT /api/rentals/{id}/contract for Admin/Manager/CustomerService to record an HTTP(S) contract link (max 255 characters) and a non-future signedAt. Only pending/pre_inspection long-term rentals may be updated. Signed evidence is immutable; repeating identical values is idempotent. GET rental returns contract metadata and unit price snapshots under existing resource authorization. No document is fetched by the server. Renewal and payment schedules remain outside this change.
