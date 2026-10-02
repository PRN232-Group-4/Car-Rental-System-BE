# 2026-10-02 — Manager rental-policy access

- **FR/NFR:** FR-007, FR-011
- **Type:** feat, fix

## Delivered

- Added the `Manager` role through immutable migration `0005_add_manager_role`.
- Granted Admin, Manager and existing Staff roles access to list, create and update rental policies. Setting `isActive` to `false` retires a policy without altering existing rentals.
- Updated the migration hosted service to apply ordered post-baseline SQL migrations and package those scripts with the Web API publish output.

## Verification

- Added a unit test that asserts each rental-policy management endpoint includes the Manager role.
