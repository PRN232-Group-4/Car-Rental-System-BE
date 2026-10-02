# 2026-10-02 — Immutable rental-policy configuration

- **FR/NFR:** FR-007, FR-011, FR-017
- **Type:** change

## Delivered

- Restricted rental-policy configuration to Admin and Manager.
- Removed the policy update endpoint and repository update operation. Policies can only be created as new versions, protecting historical rental terms.

## Verification

- Updated the authorization reflection test to assert that no `EditPolicy` API is exposed.
