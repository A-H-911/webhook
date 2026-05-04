# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

### Backend (.NET 10)

```bash
# Build entire solution
dotnet build

# Run all tests
dotnet test

# Run a single test by name
dotnet test --filter "FullyQualifiedName~PostToWebhook_Returns200_AndCreatesRequest"

# Run only unit tests
dotnet test tests/WebhookService.UnitTests/

# Run only integration tests (requires Docker for SQL Server container)
dotnet test tests/WebhookService.IntegrationTests/

# Apply EF Core migrations
dotnet ef database update --project src/WebhookService.Infrastructure --startup-project src/WebhookService.API

# Format code
dotnet format
```

### Frontend (Angular 21)

```bash
cd frontend/webhook-spa

# Install dependencies
npm install

# Start dev server (proxies /api, /hooks, /health to localhost:8080)
npm start

# Production build
npm run build

# Run unit tests
npm test
```

### E2E Tests (Playwright)

```bash
# Install browsers (first run only)
pwsh tests/WebhookService.E2ETests/bin/Debug/net10.0/playwright.ps1 install

# Run E2E tests against running stack
E2E_BASE_URL=http://localhost dotnet test tests/WebhookService.E2ETests/
```

### Docker

```bash
# Start full stack (copies .env.example to .env first time)
cp .env.example .env
docker compose up -d

# Dev override (adds CORS for Angular dev server at :4200)
docker compose -f docker-compose.yml -f docker-compose.override.yml up -d
```

## Architecture

The solution follows Clean Architecture with four layers:

```
src/
  WebhookService.Domain/          # Entities, value objects, repository interfaces, ISseNotifier
  WebhookService.Application/     # CQRS handlers (MediatR), DTOs, validation behaviors
  WebhookService.Infrastructure/  # EF Core (MSSQL), SseNotifier, RetentionCleanupService
  WebhookService.API/             # ASP.NET Core endpoints, Minimal API, Swagger, SEQ
frontend/webhook-spa/             # Angular 21 standalone components, Angular Material
tests/
  WebhookService.UnitTests/       # xUnit, domain entity tests, no infrastructure
  WebhookService.IntegrationTests/# xUnit + Testcontainers.MsSql + WebApplicationFactory
  WebhookService.E2ETests/        # Playwright headless Chromium
docker/
  sqlserver/                      # Custom SQL Server image with init.sql polling entrypoint
  frontend/                       # Nginx multi-stage Dockerfile + nginx.conf
```

## Key Non-Obvious Facts

**URL routing split:** The webhook *receiver* endpoint is `POST /hooks/{guid}`. The `webhookUrl` field shown in the UI and stored in the DB is `{baseUrl}/webhook/{guid}` — integration test helpers extract the token by splitting on `/` and taking `Last()`.

**SSE notifier is in-process:** `SseNotifier` uses `ConcurrentDictionary<Guid, List<Channel<SseEvent>>>` — no Redis, no message broker. Max 10 concurrent SSE connections per token. The SSE endpoint is `GET /api/tokens/{tokenId}/sse`.

**Retention cleanup is a BackgroundService:** `RetentionCleanupService` runs on a 24-hour `PeriodicTimer` using `IServiceScopeFactory`. Hangfire is not used in this project.

**Integration tests need Docker:** `WebAppFactory` spins up a real `mcr.microsoft.com/mssql/server:2022-latest` container via Testcontainers. Docker must be running before `dotnet test` on that project.

**Custom SQL Server Docker image:** `docker/sqlserver/Dockerfile` wraps the official image with an `entrypoint.sh` that polls until SQL Server is ready, then runs `init.sql` — necessary because the official image's `MSSQL_*` env vars don't work reliably for schema init.

**Environment config:** Copy `.env.example` to `.env` before first `docker compose up`. Key variables: `SA_PASSWORD`, `WEBHOOK_BASE_URL` (used to generate webhook URLs), `RETENTION_DAYS`, `MAX_REQUEST_SIZE_MB`.

**Angular dev proxy:** `proxy.conf.json` forwards `/api`, `/hooks`, `/health` to `http://localhost:8080`. The `ng serve` target at port 4200 uses this automatically via `angular.json`.

**PowerShell encoding:** When editing files in PowerShell 5.1, always use `[System.IO.File]::ReadAllText/WriteAllText` with explicit `System.Text.Encoding.UTF8`. Never use bare `Get-Content`/`Set-Content` on UTF-8 files — they silently corrupt non-ASCII characters.

## Docker Compose Services

| Service | Image | Port |
|---------|-------|------|
| `api` | Built from `src/WebhookService.API/Dockerfile` | 8080 |
| `frontend` | Built from `docker/frontend/Dockerfile` | 80 |
| `sqlserver` | Built from `docker/sqlserver/Dockerfile` | 1433 |
| `seq` | `datalust/seq:latest` | 5341 (ingest), 8081 (UI) |

Nginx at port 80 reverse-proxies `/api/`, `/hooks/`, and `/health` to the API container and serves the Angular SPA for all other paths with `try_files $uri $uri/ /index.html`. SSE routes (`/api/tokens/*/sse`) have `proxy_buffering off` and `proxy_read_timeout 3600s`.
