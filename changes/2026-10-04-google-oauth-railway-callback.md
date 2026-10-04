# 2026-10-04 — Google OAuth callback on Railway

- **FR/NFR:** FR-003, NFR-004, NFR-008
- **Type:** fix, deployment

## Changed

- Trust Railway's forwarded host/protocol headers before authentication, preserving the public HTTPS Google callback URL.
- Validate enabled Google OAuth configuration at startup.
- Log the underlying callback failure server-side and return a safe `400` ProblemDetails response rather than an opaque `500`.

## Deployment checks

- Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` and retain the exact redirect URI `https://<railway-domain>/signin-google`.
- If sign-in still fails, inspect the Railway deployment log for `Google OAuth callback failed.` and verify migrations through `0008_staff_authorization` have applied.
