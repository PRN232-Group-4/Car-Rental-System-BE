# Account administration and staff profiles — 2026-10-20

- Add Admin-only account directory/detail with search, role/active filters, complete roles and customer/staff profile IDs; pagination is validated at HTTP and Application boundaries with a 64-bit offset.
- Add account activation with reason, role membership changes and explicit session revocation. Audit writes and authorization-version invalidation commit with each mutation; Redis refresh sessions are then revoked.
- Protect the last active administrator using a common transaction advisory lock; reject self-deactivation and invalid role/profile combinations. Existing staff specialty semantics are preserved.
- Staff reads/writes expose stored avatarUrl, gender and address; reads include assigned trip count and current renting trip ID.
- Migration `0022_admin_user_audit.sql` adds users.address and administrative audit storage.
- Tests cover HTTP authentication/authorization, real JSON envelopes, query/body validation, pagination edges, Swagger, service validation and PostgreSQL role/profile/audit/last-admin concurrency behavior.

Validation results are recorded in the combined API-field completion change by the parent task after integrated execution.
