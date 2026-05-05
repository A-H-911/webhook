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

# Run E2E tests against running stack (set E2E_BASE_URL to match frontend port)
E2E_BASE_URL=http://localhost:8088 dotnet test tests/WebhookService.E2ETests/

# Or if frontend is running on port 80 (with override setup)
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

**URL routing:** The webhook *receiver* endpoint is `POST /webhook/{token:guid}`. The `webhookUrl` field shown in the UI and stored in the DB is `{baseUrl}/webhook/{guid}`.

**SSE endpoint:** `GET /api/tokens/{tokenId}/sse` — not `/api/events/`. Max 10 concurrent SSE connections per token. SSE response begins with `retry: 5000\n\n` before the event loop. **Wire event name is `event: request`** (not `new-request`) — `SseService` listens via `es.addEventListener('request', ...)` and maps to internal type `eventType: 'new-request'`. The `onopen` handler emits `{ eventType: 'connected' }` so the green dot appears immediately on connect, not only on first event.

**Retention cleanup is a BackgroundService:** `RetentionCleanupService` runs on a 24-hour `PeriodicTimer` using `IServiceScopeFactory`. Hangfire is not used in this project. The service wraps all cleanup work in try/catch — DB errors are logged and do not stop the timer.

**Token cache:** Uses `GetOrCreateAsync` with 60-second sliding expiration. **Null results are not cached** — cache is explicitly removed if token is not found or inactive. Custom response updates (`SetCustomResponse`, `ResetCustomResponse`, `DeleteToken`) all call `cache.Remove()`.

**Repository reads:** `WebhookTokenRepository` and `WebhookRequestRepository` use `.AsNoTracking()` on all SELECT queries.

**IDOR protection:** `GetRequestById`, `ExportRequest`, and `DeleteRequest` queries include `WHERE TokenId = @tokenId` to verify ownership before returning or modifying a request.

**HTTP 422 validation:** `GlobalExceptionMiddleware` catches `FluentValidation.ValidationException` and returns HTTP 422 with a field/message error list (not 400).

**Integration tests need Docker:** `WebApplicationFactory` spins up a real `mcr.microsoft.com/mssql/server:2022-latest` container via Testcontainers. Docker must be running before `dotnet test` on that project. Tests use a local `TestNullSseNotifier` stub for `ISseNotifier`.

**Custom SQL Server Docker image:** `docker/sqlserver/Dockerfile` wraps the official image with an `entrypoint.sh` that polls until SQL Server is ready, then runs `init.sql` — necessary because the official image's `MSSQL_*` env vars don't work reliably for schema init.

**Environment config:** Copy `.env.example` to `.env` before first `docker compose up`. Key variables: `SA_PASSWORD`, `WEBHOOK_BASE_URL` (used to generate webhook URLs), `RETENTION_DAYS`, `MAX_REQUEST_SIZE_MB`.

**Angular dev proxy:** `proxy.conf.json` forwards `/api`, `/webhook`, `/health` to `http://localhost:8080`. The `ng serve` target at port 4200 uses this automatically via `angular.json`.

**SSE disconnect handling:** `GlobalExceptionMiddleware` catches `OperationCanceledException` silently when `context.RequestAborted.IsCancellationRequested` is true — this is a normal SSE client disconnect, not an error. `WriteErrorAsync` guards with `if (context.Response.HasStarted) return;` because SSE responses have already flushed headers before the connection drops; attempting to set `StatusCode` on a started response throws `InvalidOperationException`. The `ValidationException` branch has the same guard.

**Custom response headers contract:** `SetCustomResponseRequest.Headers` (C# API model) and `SetCustomResponseDto.headers` (Angular DTO) are typed as `string` — a raw JSON string (e.g. `"{\"X-Custom\":\"value\"}"`) — **not** a `Record<string,string>` object. The dialog validates the string with `JSON.parse` before sending but passes the raw string to the API, not the parsed object. Keep both sides in sync if either is changed.

**PowerShell encoding:** When editing files in PowerShell 5.1, always use `[System.IO.File]::ReadAllText/WriteAllText` with explicit `System.Text.Encoding.UTF8`. Never use bare `Get-Content`/`Set-Content` on UTF-8 files — they silently corrupt non-ASCII characters.

## Docker Compose Services

| Service | Image | Port |
|---------|-------|------|
| `api` | Built from `src/WebhookService.API/Dockerfile` | 8080 |
| `frontend` | Built from `docker/frontend/Dockerfile` | 8088 |
| `sqlserver` | Built from `docker/sqlserver/Dockerfile` | 1433 |
| `seq` | `datalust/seq:latest` | 5341 (ingest), 5342 (UI) |

Nginx at port 8088 (mapped from internal 80) reverse-proxies `/api/`, `/webhook/`, and `/health` to the API container and serves the Angular SPA for all other paths with `try_files $uri $uri/ /index.html`. SSE routes (`~ ^/api/tokens/[^/]+/sse$`) have `proxy_buffering off` and `proxy_read_timeout 3600s`. SEQ UI is accessible at `http://localhost:5342`.
