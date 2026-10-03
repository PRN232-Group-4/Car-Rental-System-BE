# Access and refresh token cookies

- Replaced the opaque Redis cookie ticket with `access_token` JWT and `refresh_token` HttpOnly cookies.
- Added `POST /api/auth/refresh` with single-use refresh-token rotation; Redis persists only refresh token hashes.
- Token sessions are revoked by logout, logout-all, password change, and password reset; access JWT validation checks the active Redis session id.
- Google completion now issues the same token-cookie pair.
