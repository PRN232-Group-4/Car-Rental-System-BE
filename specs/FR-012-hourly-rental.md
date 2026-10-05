# FR-012 — Đơn thuê theo giờ
- **Status:** Implemented (repository workflow); **Priority:** P0; **Milestone:** M4
Tạo hourly rental có/không driver. Acceptance: valid duration, check license, price snapshot, availability transaction.

POST /api/rentals/hourly exists with transactional availability, hour_rental_detail, unit-price/VAT/deposit snapshots and direct invoice creation. GET /api/rentals/{id} also exposes hourlyPriceSnapshot and hasDriver. A dedicated Domain Rental aggregate remains an architectural follow-up.
