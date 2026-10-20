# FR-038 — Operations API completion

- **Status:** Implemented; verified HTTP/PostgreSQL integration 2026-10-20; **Priority:** P1.
- Admin/Manager moderate ratings as visible/hidden/reported with a required reason and audited actor/time, and publish a reply. Only visible ratings contribute to staff average.
- Report lifecycle adds open → in_progress → assigned/resolved and open/in_progress → rejected; rejected → in_progress allows review to resume. Status change requires a reason; breakdown rejection does not release its vehicle safety block. Assigned reports cannot bypass maintenance/recovery through this API.
- GET reports includes recorded timestamps, customer, rental, maintenance, history and notes. Missing legacy audit remains empty rather than invented. Reports created/assigned/resolved/recovered after this migration receive history transactionally.
- Report maintenance assignment accepts optional maintenanceType and description, defaulting to incident/report description. Existing safety and assignment transaction boundaries remain.
- Reports, ratings, maintenance and driver schedule HTTP lists use page=1/pageSize=20, ranges 1+/1–100, stable ordering and totalCount after filtering. Maintenance accepts from/to on createdAt, end-exclusive UTC timestamps.

Acceptance: real PostgreSQL tests verify enrichment, transition/audit atomicity, hidden rating aggregates, maintenance override and pagination.
