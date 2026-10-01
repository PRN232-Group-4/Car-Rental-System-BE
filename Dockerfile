FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/VehicleRental.WebApi/VehicleRental.WebApi.csproj", "src/VehicleRental.WebApi/"]
COPY ["src/VehicleRental.Application/VehicleRental.Application.csproj", "src/VehicleRental.Application/"]
COPY ["src/VehicleRental.Infrastructure/VehicleRental.Infrastructure.csproj", "src/VehicleRental.Infrastructure/"]
COPY ["src/VehicleRental.Domain/VehicleRental.Domain.csproj", "src/VehicleRental.Domain/"]
RUN dotnet restore "src/VehicleRental.WebApi/VehicleRental.WebApi.csproj"
COPY . .
RUN dotnet publish "src/VehicleRental.WebApi/VehicleRental.WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN adduser --disabled-password --gecos "" --uid 10001 appuser && chown -R appuser /app
USER appuser
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet VehicleRental.WebApi.dll"]
