# FR-030 — Dashboard admin
- **Status:** In progress; **Priority:** P2; **Milestone:** M7
Revenue/renting/maintenance/subscription metrics. Acceptance: RBAC, date/timezone/filter, query performance.

## Phân quyền cập nhật 2026-10-03

Phân quyền GET dashboard: Admin/Manager; không cấp cho Staff chung.

## Analytics contract — 2026-10-20

- GET `/api/dashboard` returns typed `revenue`, `renting`, `maintenance`, `activeSubscriptions` and `previous` metrics.
- GET `/api/dashboard/revenue-series`, `/top-vehicles`, `/maintenance-vs-revenue`, `/plan-usage` accept `from`, `to`, `facilityId`, `vehicleTypeId`, `page=1`, `pageSize=20` (1–100). Lists return items/page/pageSize/totalCount, current/previous period metadata and previous values per item.
- Periods use UTC and half-open `[from,to)` boundaries. Default: trailing 30 days ending now. Maximum 366 days. Previous period is the adjacent interval of equal duration. Offset-bearing inputs normalize to UTC; inputs without offset are UTC.
- Revenue follows the existing dashboard definition: paid invoice amounts grouped by invoice issue timestamp, with vehicle type grouping. It excludes subscription purchases and does not imply cash collection or net profit. Daily series uses UTC calendar dates, clips first/last bucket at the requested bounds, and includes zero-revenue dates. Each row exposes `period` and `previousPeriod` boundaries: previous row intervals shift current bucket bounds backward by the whole requested duration, preserving comparison totals for partial days.
- Vehicle hours are actual rental overlap (started_at through returned_at/completed_at, clipped to now and the period); rentals count actual overlaps. Utilization is hours / period hours × 100. Maintenance costs use completed work, dated by end_time (created_at fallback for legacy work).
- Snapshot operational counts are reconstructed at the period end using rental start/return and maintenance start/end timestamps. `asOf`/`previousAsOf` expose the observed instant: the earlier of the respective period end and now, because future operational state is unknown. Cancelled maintenance and legacy completed maintenance lacking an end timestamp are excluded; historical cancellations cannot be reconstructed without an audit ledger. Subscription membership uses start/end dates for active/expired subscriptions; facility/type filters scope subscriptions to those with matching rentals in the period. Plan activeSubscribers counts distinct customers with an overlapping subscription; snapshot activeSubscriptions counts memberships.
- Plan usedHours sums booked subscription hours for non-cancelled rentals starting in the period plus recorded overtime_hours; includedHours is the allowance summed across overlapping subscriptions (null for unlimited), not a prorated monthly quota. Usage percentage is null for unlimited or zero allowance. This measures booked consumption because the database has no immutable subscription deduction ledger.
- Acceptance: Management RBAC, typed Swagger/envelope, date/id/page validation at HTTP and Application, deterministic sort, filtered counts, empty pages and SQL aggregation tests.


Kiểm chứng 2026-10-20: build solution -warnaserror và bộ 325 test HTTP/Application/PostgreSQL liên quan đạt, gồm các acceptance ở trên. Database tạm độc lập; chưa deploy hoặc áp dụng migration vào database ứng dụng.
