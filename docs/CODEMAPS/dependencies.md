<!-- Generated: 2026-05-10 | Updated: Redis service + stream-worker + jobs-worker; StackExchange.Redis; AspNetCore.HealthChecks.Redis; WindowsServices; WEBHOOK_WORKER_ID; curl in runtime image -->

# Dependencies

## Backend (.NET 10)

### Core Framework
- `Microsoft.AspNetCore` — ASP.NET Core 10 Web API
- `Microsoft.EntityFrameworkCore.SqlServer` — ORM + SQL Server provider
- `Microsoft.EntityFrameworkCore.Design` — EF migrations tooling

### CQRS / Validation
- `MediatR` — CQRS command/query bus + pipeline behaviors
- `FluentValidation.AspNetCore` — request validation (auto-discovered in Application assembly)

### Redis (Added as part of three-process split)
- `StackExchange.Redis` — `IConnectionMultiplexer`; stream publisher, token cache, SSE bridge, stream consumer

### Auth
- `Microsoft.AspNetCore.Authentication.Cookies` — cookie-based session auth
- `BCrypt.Net-Next` — password hash verification at login

### Resilience & Security
- `Polly` — retry: startup DB readiness (workers, 30x exponential) + API migration (5x)
- `Microsoft.AspNetCore.RateLimiting` — fixed-window (login 5/min, webhook-receiver configurable)
- `Microsoft.AspNetCore.Antiforgery` — CSRF protection, X-XSRF-TOKEN validation

### Health Checks
- `AspNetCore.HealthChecks.SqlServer` — SQL ping for /health/ready (all three units)
- `AspNetCore.HealthChecks.Redis` — Redis ping for /health/ready (StreamWorker only)
- `Microsoft.Extensions.Hosting.WindowsServices` — Windows Service SCM support (workers)

### Observability
- `Serilog.AspNetCore` — structured logging
- `Serilog.Sinks.Seq` — log shipping to SEQ
- `Microsoft.AspNetCore.Diagnostics.HealthChecks` — /health/live + /health/ready

### Testing
- `xUnit` — test framework (unit + integration + E2E)
- `NSubstitute` — mocking (unit tests)
- `Testcontainers.MsSql` — real SQL Server 2022 in integration tests
- `Microsoft.AspNetCore.Mvc.Testing` — `WebApplicationFactory<Program>`
- `Microsoft.Playwright` — headless Chromium E2E tests

## Frontend (Angular 21)

### Core
- `@angular/core` 21.x — standalone components, signals
- `@angular/router` — lazy-loaded routes
- `@angular/common/http` — `HttpClient` + interceptors
- `@angular/platform-browser` — `provideAnimationsAsync`

### UI
- `@angular/material` — mat-dialog, mat-list, mat-table, mat-paginator, mat-toolbar

### Build / Dev
- `@angular/cli` — `ng serve` (proxy to :8080), `ng build`
- `@angular/build` — esbuild-based builder

### Testing
- `Karma` + `Jasmine` — Angular unit tests (`npm test`)

## Infrastructure Services

| Service | Image | Host Port | Purpose |
|---------|-------|-----------|---------|
| `api` | Custom .NET (`PROJECT_NAME=WebhookService.API`) | 8080 | Backend API + SSE fan-out |
| `stream-worker` | Custom .NET (`PROJECT_NAME=WebhookService.StreamWorker`) | none | Redis stream consumer → SQL persist |
| `jobs-worker` | Custom .NET (`PROJECT_NAME=WebhookService.JobsWorker`) | none | Retention cleanup (single replica) |
| `sqlserver` | Custom (SQL Server 2022) | 1433 | Primary data store |
| `redis` | `redis:7-alpine` | 6379 (localhost only) | Stream + pub/sub + token cache |
| `seq` | `datalust/seq:latest` | 5341 (ingest), 5342 (UI) | Structured log aggregation |
| `frontend` | Custom nginx | 8088 | Static SPA + reverse proxy |

**Runtime image:** `mcr.microsoft.com/dotnet/aspnet:10.0` — `curl` installed via `apt-get` for Docker health checks.
**Dockerfile:** Parameterized with `ARG PROJECT_NAME=WebhookService.API` — single file builds all three .NET services.

## Tools

### RotatePassword
- CLI for generating BCrypt password hashes
- Usage: `dotnet run --project tools/RotatePassword -- --password 'mypassword'`
- Output: BCrypt hash (starts with `$2`)
- Warning: Single-quote the hash in `.env` — `AUTH_PASSWORD_HASH='$2b$12$...'`
  Dollar signs followed by letters (e.g. `$fekMo4`) are interpolated as variables by Docker Compose

## Key Config / Env
```
WEBHOOK_BASE_URL          — public base URL for webhook URLs (empty in appsettings.json → validator fires)
                            Dev:   appsettings.Development.json → http://localhost:8080
                            Local: set in .env → http://localhost:8088
                            ngrok: set in .env → https://your-domain.ngrok.app

AUTH_USERNAME             — single admin username
AUTH_PASSWORD_HASH        — BCrypt hash, single-quoted in .env to prevent $ interpolation
AUTH_SESSION_HOURS        — cookie sliding expiry (default 8)
CORS_ALLOWED_ORIGINS      — comma-separated origins (dev: http://localhost:4200)

WEBHOOK_WORKER_ID         — StreamWorker Redis consumer group name
                            Compose: stream-worker-1 | Fallback: consumer-{MachineName}
                            Must be stable across restarts — changing it orphans PEL entries

ConnectionStrings__WebhookDb  — MSSQL connection string
ConnectionStrings__Redis      — Redis host:port (e.g. redis:6379)
Webhook:RetentionDays         — request retention in days (default 7)
Webhook:MaxRequestSizeMb      — Kestrel body size limit, API only (default 5)
Webhook:ReceiverRateLimitPerSecond — webhook receiver rate limit (default 5/sec)
```
