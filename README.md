# Car Rental System – Backend

ASP.NET Core 8 Web API theo Clean Architecture (Domain → Application → Infrastructure → WebApi), dùng PostgreSQL và Redis.

```text
src/
├── VehicleRental.Domain/          # Quy tắc nghiệp vụ thuần
├── VehicleRental.Application/     # Use case, DTO, port
├── VehicleRental.Infrastructure/  # PostgreSQL, Redis, payOS, Cloudinary, Brevo, Mapbox
└── VehicleRental.WebApi/          # Controllers, middleware, Program.cs
tests/VehicleRental.Tests/         # xUnit (một phần cần PostgreSQL local)
```

## Chạy local

```bash
docker compose up -d db redis
dotnet run --project src/VehicleRental.WebApi
```

## Tài liệu

- [Luồng hoạt động](docs/flow.md)
- [Kiến trúc](docs/architecture.md)
- [API](docs/API.md)
- [Mục lục tài liệu](docs/README.md)
- [Quy ước cho agent/dev](AGENTS.md)
