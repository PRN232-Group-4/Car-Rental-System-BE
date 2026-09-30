FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Car-Rental-System-BE.csproj", "./"]
RUN dotnet restore "Car-Rental-System-BE.csproj"
COPY . .
RUN dotnet publish "Car-Rental-System-BE.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN adduser --disabled-password --gecos "" --uid 10001 appuser && chown -R appuser /app
USER appuser
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet Car-Rental-System-BE.dll"]
