# 2026-10-01 — PayOS payment scaffold

- **FR/NFR:** FR-020, FR-021, NFR-004
- **Type:** feat

## Delivered

- Added provider-neutral checkout and webhook contracts, plus a PayOS REST adapter.
- Checkout is disabled safely without keys; it returns `503` and never fabricates a payment URL.
- Added signature verification, webhook idempotency via provider reference, invoice-payment persistence and no-over-refund validation.
- Added an admin/staff endpoint that persists refund requests for a future payout worker.
- Added `PayOs` environment-variable template in `.env.example`.

## Deployment

- Apply `db/migrations/0003_payment_provider_reference.sql` after migration 0002.
- Set the six `PayOs__*` values, including public HTTPS return/cancel URLs; then change `PayOs__Enabled` to `true`.

## Verification

- `dotnet build Car-Rental-System-BE.sln --no-restore` succeeds with zero warnings/errors.
