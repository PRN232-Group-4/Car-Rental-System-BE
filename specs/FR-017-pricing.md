# FR-017 — Phí, cọc, tổng tiền
- **Status:** In progress; **Priority:** P0; **Milestone:** M4
Tính VAT/deposit/overtime theo policy snapshot. Acceptance: decimal precision, deterministic tests.

Unit price, VAT and deposit amounts are persisted at booking; referenced policy versions are immutable. Rental detail now exposes hourly/monthly unit-price snapshots. Automated overtime calculation remains unimplemented; do not mark FR-017 complete.
