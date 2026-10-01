# 2026-10-01 — Core catalog and rental APIs

- **FR/NFR:** FR-006, FR-008..018, FR-022..023, FR-026, FR-030
- **Type:** feat

## Delivered

- Added customer driving-license submission and staff review.
- Added protected department, facility, vehicle type, vehicle and rental-policy CRUD.
- Added available-vehicle search, hourly/long-term rental creation, collision checks, pricing snapshots, direct-rental invoice creation, assignment, inspections, lifecycle transitions, subscription-plan purchase, maintenance and dashboard endpoints.
- Added initial `license_status` and idempotent role seed to the fresh-schema SQL.

## Not yet delivered

- Deposit payout execution (refund request is persisted), report attachments/lifecycle, loyalty ledger, notification outbox and subscription-expiry worker need dedicated designs and tests.

## Verification

- `dotnet build Car-Rental-System-BE.sln --no-restore` succeeds with zero warnings/errors.

## Assumptions

- API timestamps are UTC. An available-vehicle query excludes non-finalized overlapping rentals.
- Existing databases must add `customers.license_status` before deploying this version; the initial schema change only applies to new databases.
- PayOS checkout and webhook contracts are present but remain disabled until payment-channel keys and public return/cancel URLs are configured.
