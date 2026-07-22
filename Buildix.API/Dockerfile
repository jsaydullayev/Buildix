# syntax=docker/dockerfile:1
# Multi-stage build for the Buildix .NET 9 API.
# Build context MUST be the repo root (the API references the Domain/Application/
# Infrastructure projects): docker-compose sets `context: .` + this dockerfile.

# ── build ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first, using only the .csproj files, so `dotnet restore` is cached
# and only re-runs when a project file changes (not on every source edit).
COPY Buildix.Domain/Buildix.Domain.csproj Buildix.Domain/
COPY Buildix.Application/Buildix.Application.csproj Buildix.Application/
COPY Buildix.Infrastructure/Buildix.Infrastructure.csproj Buildix.Infrastructure/
COPY Buildix.API/Buildix.API.csproj Buildix.API/
RUN dotnet restore Buildix.API/Buildix.API.csproj

# Copy the sources and publish a framework-dependent build (no self-contained
# apphost — the aspnet runtime image provides the host).
COPY Buildix.Domain/ Buildix.Domain/
COPY Buildix.Application/ Buildix.Application/
COPY Buildix.Infrastructure/ Buildix.Infrastructure/
COPY Buildix.API/ Buildix.API/
RUN dotnet publish Buildix.API/Buildix.API.csproj -c Release -o /app/publish /p:UseAppHost=false

# ── runtime ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl for the container HEALTHCHECK (the aspnet image ships neither curl nor wget).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# Product images live under wwwroot/uploads; compose mounts a named volume here.
# chown so the non-root `app` user (and the freshly-initialised named volume,
# which inherits this dir's ownership on first mount) can write uploads.
RUN mkdir -p /app/wwwroot/uploads && chown -R app:app /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    PORT=8080
EXPOSE 8080

# Kestrel binds 0.0.0.0:8080 (Program.cs reads PORT). nginx proxies to api:8080.
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

USER app
ENTRYPOINT ["dotnet", "Buildix.API.dll"]
