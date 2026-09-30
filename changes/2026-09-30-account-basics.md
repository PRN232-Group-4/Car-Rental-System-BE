# Account basic APIs

## Implemented
- Added registration resend, password-reset OTP, local cookie login/logout/logout-all, profile read/update, and password change endpoints.
- Added PostgreSQL account lookup/update port and Redis session-user index so password changes/reset can revoke every session.
- Added a configurable 24-hour absolute session ceiling in addition to the existing 8-hour sliding cookie expiry.
- Newly registered and Google users are assigned the `Customer` role idempotently.
- Removed committed runtime credentials from `appsettings.json`; deployment must supply environment secrets shown in `.env.example`.

## Known gaps
- Distributed request rate limiting, CSRF protection, password-change audit logs, RBAC policies, customer license workflow, and integration tests remain pending.

## Deployment fix
- Disabled the startup migration hosted service. Railway now starts against an existing PostgreSQL schema and does not read or validate `docs/db/Init.sql`.
