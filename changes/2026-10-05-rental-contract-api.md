# Rental contract evidence and price snapshot responses

BE-12/13/15 audit: booking and pre-inspection APIs already existed; contract metadata had no write API and rental responses omitted unit-price snapshots.

- Add authorized PUT /api/rentals/{id}/contract with URL/date validation, transaction lock, immutable signed evidence and idempotent retries.
- Include unit-price snapshots, hasDriver and contract evidence in existing rental reads/responses without changing ownership checks.
- Add integration coverage for persistence, ownership, immutability, validation, interval conflicts and preserved pricing, plus role authorization tests.
- No database migration. See docs/rental-contract-api.md for API usage and remaining scope.
- Verification: build/test with warnings as errors, 131/131 tests passed on isolated PostgreSQL 18. Fixed shared vehicle seed missing required type/fuel fields. Docker deployment was not tested.
