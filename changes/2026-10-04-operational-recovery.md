# 2026-10-04 — Vehicle plates, duty shifts and incident recovery

O2/O4/O5/BE-24 previously lacked vehicle replacement, independent driver shifts and staff/driver incident reports. Vehicle APIs now expose licensePlate; Manager dispatches duty shifts and assignments, routes reports to qualified Maintenance staff, and recovers rentals using inspected vehicles with exactly matching seats and a new driver during breakdowns. Original rental pricing and invoices are preserved.

Inspection records safety and authenticated staff identity, blocks unsafe vehicles and leaves failed pre-inspections pending. It no longer cancels the rental or automatically creates maintenance/refunds; staff report damage separately. Maintenance technicians update only assigned work; managers control assignment. Workflow mutations use a shared transaction-scoped PostgreSQL advisory lock, with replacement history and rollback safety.

- Migration: 0011, tolerant of an existing manually added license_plate. Existing shifts are not inferred; managers must schedule drivers before new assignments/start.
- Specs: FR-009/014/015/016/021/026/027/031; [API guide](../docs/operational-recovery.md).
- Validation: build and PostgreSQL integration/authorization suite. A disposable local cluster is used; no Neon migration performed.
- Limits: replacement requires populated car seat metadata; Customer reporting, debt collection and automatic damage billing remain separate. The shared dispatcher lock favors consistency over write throughput for this single-fleet system.

Validation result: dotnet build --no-restore -warnaserror passed (0 warnings/errors); dotnet test passed 101/101 against disposable PostgreSQL 18 on localhost:55432. Git diff whitespace check passed. Docker deployment was not run (daemon unavailable).
