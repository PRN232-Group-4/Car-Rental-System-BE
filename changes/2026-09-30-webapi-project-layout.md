# WebApi project moved into src/

## Implemented
- Moved the WebApi layer (`Program.cs`, `Controllers/`, `Properties/`, `.http`, project file) from the repo root into `src/VehicleRental.WebApi/`, so all four layers now live under `src/`.
- Removed the `<Compile Remove="src\**\*.cs" />` workaround from the WebApi csproj (no longer needed once the project sits under `src/`).
- Replaced the `.slnx` with a classic `Car-Rental-System-BE.sln` listing all four projects — SDK 8.0 cannot parse the `.slnx` format.
- Dockerfile: copy all four csproj files before `dotnet restore`; publish path and `ENTRYPOINT` updated to `VehicleRental.WebApi.dll`.
- CI: pinned `setup-dotnet` to `8.0.x` (was `9.0.x`, conflicting with `global.json` 8.0.408).

## Test
- `dotnet build Car-Rental-System-BE.sln` → 0 warnings / 0 errors.
- `dotnet publish` produces `VehicleRental.WebApi.dll`, matching the Docker `ENTRYPOINT`.
- Docker build not run end-to-end locally (daemon unavailable); restore/publish steps verified.

## Quyết định/giả định
- Supersedes the partial `Controllers/ → src/Controllers/` move from `fix: structure` (3346911); kept the full move into `src/VehicleRental.WebApi/` while preserving that commit's Redis log fix and docs updates.
