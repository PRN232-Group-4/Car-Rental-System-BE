# FR-037 — Account administration

Admin can list/search all accounts (including inactive accounts), inspect their full roles and customer/staff profile IDs, activate/deactivate an account with a reason, add/remove a known role and revoke another user's sessions. All endpoints require Admin. Lists use page=1/pageSize=20, page >=1, pageSize 1..100 and stable ID ordering.

Account mutations are audited with actor, target, action, reason and timestamp. Role mutations and deactivation serialize on a database advisory lock so the last active Admin cannot be removed or deactivated. Self-deactivation is rejected. A role requires its corresponding existing profile; driver role additionally requires a valid driver license. Staff specialty roles are mutually exclusive, matching the existing Staff editor. Removing the Staff marker while a specialty remains is rejected. Driver membership changes synchronize staffs.is_driver under the dispatcher lock so assignment eligibility matches the user's new roles. Mutations invalidate authorization versions and revoke Redis sessions.

Staff read models additionally expose avatarUrl, gender, address, totalTrips (distinct assigned rentals) and activeTripId (currently renting assigned rental). No synthetic profile values are returned.

## Acceptance
- [x] Admin-only routes expose paginated account data without password hashes.
- [x] HTTP and Application pagination validation and known-role validation.
- [x] Safe role/profile validation, last administrator protection and mutation audit.
- [x] Status, role and explicit session revocation invalidate previous access/refresh credentials.
- [x] Customer/staff IDs allow navigating between account administration and existing profile actions.


Kiểm chứng 2026-10-20: build solution -warnaserror và bộ 325 test HTTP/Application/PostgreSQL liên quan đạt, gồm các acceptance ở trên. Database tạm độc lập; chưa deploy hoặc áp dụng migration vào database ứng dụng.
