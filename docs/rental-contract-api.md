# Rental APIs (BE-12, BE-13, BE-15)

- POST `/api/rentals/hourly`: Customer books hourly; unit price, VAT and deposit are stored at booking.
- POST `/api/rentals/longterm`: Customer books a long-term interval; overlapping active rentals are rejected transactionally.
- POST `/api/rentals/{id}/inspections/pre`: Admin/Maintenance with staff identity records inspection. Failure blocks the vehicle; passed inspection for the current vehicle is required before `renting`.
- PUT `/api/rentals/{id}/contract`: Admin/Manager/CustomerService records signed long-term evidence, while pending/pre_inspection.

```json
{
  "contractLink": "https://example.com/contracts/rental-42.pdf",
  "signedAt": "2026-10-05T03:00:00Z"
}
```

Use the actual signing time in UTC; future/missing dates and non-HTTP(S) links are rejected. Link length is limited to 255. The server stores a reference without uploading/downloading a document. Once signed, evidence cannot be replaced; identical retries are idempotent.

GET `/api/rentals/{id}` and booking/contract responses include `hourlyPriceSnapshot`, `monthlyPriceSnapshot`, `hasDriver`, `contractLink`, `signedAt` (nullable by rental type). Existing rental owner/driver/staff authorization applies. Success and errors use `{ code, status, is_success, data, message, error }`; `is_success` is a boolean and errors retain ProblemDetails inside `error`. See [API response contract](API.md). Contract PUT returns 200; invalid input/state 400, missing rental 404, unauthenticated 401, wrong role 403.

No schema migration is required. Renewal, automated overtime billing and payment schedules remain separate work. Current rental workflows live in the repository; extracting a dedicated Domain aggregate is still pending.
