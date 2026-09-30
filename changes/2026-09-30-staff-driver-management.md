# Staff and driver management APIs

## Implemented
- Added Admin-only staff create, read, update, soft-deactivate, and paginated search endpoints.
- Added a paginated driver endpoint: `GET /api/staff/drivers?search=&page=1&pageSize=20`.
- Creating a staff account hashes its password and assigns the `Staff` role; driver staff also receive `Driver`.
- Updates synchronize the `Driver` role with the `is_driver` flag, validate driver-license input, and verify referenced department/facility IDs in the database transaction.

## Assumption
- Deleting staff is implemented as `users.is_active = false` to preserve rental, inspection, and assignment history. Inactive staff cannot log in and are excluded from lists.

## Remaining work
- Department/facility CRUD, Admin bootstrap/seeding, and automated database-backed endpoint tests.
