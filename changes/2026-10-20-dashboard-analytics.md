# Typed dashboard and analytics — 2026-10-20

Adds Management-only typed snapshot and paginated revenue-series, top-vehicles, maintenance-vs-revenue and plan-usage endpoints. All accept UTC periods and facility/type scope. Current/adjacent previous metrics come from PostgreSQL aggregates, with explicit revenue, actual utilization, completed-maintenance cost and booked-subscription usage definitions in FR-030.

DashboardFilter validates IDs, period length/order and pagination in both MVC and Application. Page/count share one SQL snapshot; offsets use long arithmetic. Unlimited subscription allowance and usage percentage remain null.

Revenue buckets use UTC calendar boundaries clipped to the requested period. Per-row current/previous boundaries preserve comparison totals even for partial days. Snapshot includes asOf/previousAsOf so future-period requests explicitly expose the available observation instant.

Validation added: actual HTTP envelope/auth/filter/pagination/Swagger tests plus PostgreSQL tests for dates, paid-only revenue, previous periods, empty buckets/pages, no invoice join multiplication, actual overlap hours, completed costs and filtered/unlimited subscriptions. Execution results are reported in the parent integration change.
