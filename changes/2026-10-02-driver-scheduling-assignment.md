# 2026-10-02 — Driver scheduling and inspected-rental assignment

- **FR/NFR:** FR-007, FR-014, FR-015
- **Type:** feat

## Delivered

- Added an Admin/Manager driver-schedule API and granted Manager access to the rental driver-assignment API.
- Assignment now requires a passed pre-rental inspection, an active qualified driver with an unexpired license, and no overlapping active assignment.
- Added a partial unique index to retain at most one active assignment per rental; legacy duplicates are preserved as `reassigned` history, and each new assignment records the authenticated operator email.

## Assumption

- A driver is assigned after successful pre-inspection, while rental status is `pre_inspection`, before transition to `renting`.
