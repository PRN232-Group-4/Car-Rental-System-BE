# FR-041 — Role catalog administration

- **Status:** Implemented and verified locally
- **Priority:** P1

Admin manages the persisted role catalog. Existing role names used by authorization policies are protected: Admin, Manager, Staff, CustomerService, Driver, Maintenance and Customer. Custom roles can be assigned/removed through existing user administration endpoints; a custom name does not implicitly grant built-in policy capabilities.

| Method | Path | Auth | Behavior |
|---|---|---|---|
| GET | /api/roles | RequireAdmin | Search and paginate roles, defaults page=1/pageSize=20, page>=1 and pageSize 1..100. |
| GET | /api/roles/{id} | RequireAdmin | Read one persisted role; missing ID returns 404. |
| POST | /api/roles | RequireAdmin | Create a custom role; returns 201 and Location. |
| PUT | /api/roles/{id} | RequireAdmin | Update an unused custom role name. |
| DELETE | /api/roles/{id} | RequireAdmin | Delete an unused custom role; returns 200 with the shared envelope. |

Requests supply `name`, trimmed and required, at most 255 characters. Role names are unique without case sensitivity. Response exposes ID/name and system-role/membership metadata. Renaming/deleting a built-in role or a custom role assigned to users returns 409. Duplicate names return 409, including concurrent requests. References are checked in a transaction because the schema has no foreign keys. An existing role is resolved to its stored name before assignment; unknown roles return 400. Built-in profile/specialty/last-admin checks and session invalidation are preserved.

The list has stable ID ordering, count after filtering, 64-bit offset and empty items for out-of-range pages. HTTP and Application validate inputs. All responses use the shared six-field envelope with contextual messages; Swagger documents authorization, pagination and error status codes. Migration adds database-level case-insensitive uniqueness without silently deleting legacy data.

## Acceptance

- [x] Admin-only CRUD with real JSON envelope, validation, status codes and Swagger tested.
- [x] Application rejects invalid input and protects system roles.
- [x] PostgreSQL verifies CRUD, duplicate races, assignments, reference protection and pagination.
- [x] Existing user administration protections and architecture tests still pass.

Validation: solution build with `-warnaserror` and 197/197 related HTTP/Application/PostgreSQL tests passed without skips. See [change](../changes/2026-10-21-audit-logs-role-crud.md). No deployment or migration of the application database was performed.
