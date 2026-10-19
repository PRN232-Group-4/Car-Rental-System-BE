# FR-018 — Rental handover API

- Status: Implemented; scope: staff handover queue backed by the existing rental lifecycle.
- Admin, Manager and CustomerService can list handovers, view a rental's handover detail and confirm successful handover.
- `GET /api/handovers?status=pending|completed|all`: pending maps to rental `pre_inspection`; completed includes rentals with started_at and status renting/post_inspection/completed, preserving handover history after return. Default pending; all combines both sets.
- Optional filters: customerId, vehicleId, from, to. Pagination defaults to page=1/pageSize=20, accepts 1–100 per page, rejects invalid values with 400 at HTTP and Application boundaries. Response has items/page/pageSize/totalCount and stable rental ID ordering.
- `GET /api/handovers/{rentalId}` returns operational rental detail for pending or completed handovers; other states return 404.
- `POST /api/handovers/{rentalId}/confirm` delegates to the rental start use case, requires no body and transitions pre_inspection -> renting. It requires a passed inspection and active assignment for the current vehicle and a valid self-drive customer license. Repeated confirmation returns 409 ALREADY_IN_STATE.
- No new table or lifecycle state. Handover response status `completed` means rentalStatus `renting`; the rental itself is not completed.
- Inspection must work after rental confirmation: allow pre-rental evidence in pending or pre_inspection, and post-rental evidence in renting or post_inspection. Preserve legacy inspection-driven transitions only from pending/renting, and preserve the already-confirmed/returned status otherwise. Evidence remains immutable.
- Start must recheck current vehicle, inspection, assignment, license, direct-rental paid invoice and vehicle availability/maintenance under the same transaction locks as dispatch/assignment mutations before updating the rental and vehicle. Old vehicle evidence cannot authorize start; legacy inspection must not bypass payment confirmation. Subscription rentals do not require an invoice.

Acceptance: queue pagination and filters, invalid requests, missing/invalid handover detail, successful confirmation and lifecycle errors, confirm -> inspection -> assignment -> handover, legacy pending inspection, inspection after return, stale vehicle evidence and assignment rejection. All API responses use the six-field envelope.
