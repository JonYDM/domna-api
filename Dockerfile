# Dockerfile multi-stage para domna-api (.NET 8). Railway lo detecta y construye.

# ── Build ──
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia los csproj primero para cachear el restore.
COPY Domna.sln ./
COPY src/Domna.Domain/Domna.Domain.csproj src/Domna.Domain/
COPY src/Domna.Application/Domna.Application.csproj src/Domna.Application/
COPY src/Domna.Infrastructure/Domna.Infrastructure.csproj src/Domna.Infrastructure/
COPY src/Domna.Api/Domna.Api.csproj src/Domna.Api/
RUN dotnet restore src/Domna.Api/Domna.Api.csproj

# Copia el resto y publica.
COPY . .
RUN dotnet publish src/Domna.Api/Domna.Api.csproj -c Release -o /app --no-restore

# ── Runtime ──
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Railway inyecta PORT; el Program lo lee y escucha en 0.0.0.0:$PORT.
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Domna.Api.dll"]
