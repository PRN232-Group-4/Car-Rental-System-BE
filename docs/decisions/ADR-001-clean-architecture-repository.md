# ADR-001 — Clean Architecture và repository theo aggregate
- Status: Accepted
- Ngày: 2026-09-30
## Bối cảnh
Cần tách nghiệp vụ thuê xe khỏi PostgreSQL/Redis/Brevo; schema không có FK.
## Quyết định
Bốn layer Domain, Application, Infrastructure, Web API. Repository theo aggregate root, không generic repository. Application kiểm tra toàn vẹn logic, Infrastructure bao transaction.
## Hệ quả
Code có thêm port/implementation nhưng test được use case độc lập; không được nhét SQL vào controller.
