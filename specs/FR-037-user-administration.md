# FR-037 — Account administration

Admin can list/search all accounts (including inactive accounts), inspect their full roles and customer/staff profile IDs, activate/deactivate an account with a reason, add/remove an existing role and revoke another user's sessions. Account administration endpoints require Admin. Lists use page=1/pageSize=20, page >=1, pageSize 1..100 and stable ID ordering. Custom role membership uses the role catalog in FR-041; built-in profile and specialty requirements remain mandatory.

Account mutations are audited with actor, target, action, reason and timestamp. Role mutations and deactivation serialize on a database advisory lock so the last active Admin cannot be removed or deactivated. Self-deactivation is rejected. A role requires its corresponding existing profile; driver role additionally requires a valid driver license. Staff specialty roles are mutually exclusive, matching the existing Staff editor. Removing the Staff marker while a specialty remains is rejected. Driver membership changes synchronize staffs.is_driver under the dispatcher lock so assignment eligibility matches the user's new roles. Mutations invalidate authorization versions and revoke Redis sessions.

## Audit log directory — 2026-10-21

`GET /api/audit-logs` uses the Management policy (Admin or Manager). Optional `search` matches actor/target email, name or ID and audit ID; optional `action` filters the public action name. Defaults are `page=1`, `pageSize=20`, with page >=1 and pageSize 1..100 validated at HTTP and Application boundaries. Count is after filtering; ordering is timestamp descending then audit ID descending, with a 64-bit offset and empty items for out-of-range pages.

Items expose `id` (`AUD-{id}`), UTC `timestamp`, `actorId`, nullable `actorEmail`/`actorName`, `action`, `targetUserId`, nullable `targetUser`, nullable `details`, nullable `ipAddress` and `status`. Legacy actions map `role:add` to `role_assigned`, `role:remove` to `role_removed`, `deactivate` to `account_locked`, `activate` to `account_unlocked`, and `sessions:revoke` to `session_revoked`. Details come from the persisted reason, without fabricated text. Existing successful mutation rows have status `success`; no failed event is fabricated. Legacy rows have null IP; new account-administration mutations capture the request remote IP using the application's existing forwarded-header configuration. Audit records remain read-only through this API.

The response uses the shared six-field envelope, with `data.items`, `data.totalCount`, `data.page`, `data.pageSize` and operation-specific message. Unknown well-formed action values produce an empty filtered page.

- [x] Audit HTTP authorization, validation, JSON envelope and Swagger verified.
- [x] Audit filters, totals, stable ordering, legacy mapping and IP persistence verified with PostgreSQL.

Validated 2026-10-21: solution build with `-warnaserror` and 197/197 related tests passed without skips, including audit, role catalog, account administration, authorization, architecture and response contract. See [change](../changes/2026-10-21-audit-logs-role-crud.md).

Staff read models additionally expose avatarUrl, gender, address, totalTrips (distinct assigned rentals) and activeTripId (currently renting assigned rental). No synthetic profile values are returned.

## Acceptance
- [x] Admin-only routes expose paginated account data without password hashes.
- [x] HTTP and Application pagination validation and known-role validation.
- [x] Safe role/profile validation, last administrator protection and mutation audit.
- [x] Status, role and explicit session revocation invalidate previous access/refresh credentials.
- [x] Customer/staff IDs allow navigating between account administration and existing profile actions.


Kiểm chứng 2026-10-20: build solution -warnaserror và bộ 325 test HTTP/Application/PostgreSQL liên quan đạt, gồm các acceptance ở trên. Database tạm độc lập; chưa deploy hoặc áp dụng migration vào database ứng dụng.
