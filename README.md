# Car Rental System – Backend

ASP.NET Core 8 Web API theo Clean Architecture (Domain → Application → Infrastructure → WebApi), dùng PostgreSQL và Redis.

```text
src/
├── VehicleRental.Domain/          # Features/<Module>: quy tắc nghiệp vụ thuần
├── VehicleRental.Application/     # Features/<Module>: use case, DTO, port
├── VehicleRental.Infrastructure/  # Features/<Module>: adapter; Persistence/Rows: EF mapping
└── VehicleRental.WebApi/          # Features/<Module>: endpoint; Extensions: composition
tests/
├── VehicleRental.Domain.Tests/
├── VehicleRental.Application.Tests/
├── VehicleRental.Integration.Tests/  # PostgreSQL tạm qua TEST_DATABASE_ADMIN
└── VehicleRental.Architecture.Tests/
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
