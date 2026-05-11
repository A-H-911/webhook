<!-- Generated: 2026-05-11 | Verified: Vitest 4.0.8 + @vitest/coverage-v8 4.1.5; jsdom 28.0.0; coverageThresholds enforced in angular.json; ArchUnitNET 0.13.3 + NetArchTest.eNhancedEdition 1.4.5 architecture test libs added; FluentAssertions aligned to 8.9.0 across all test projects -->

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
- `xUnit` — test framework (unit + integration + E2E + architecture)
- `FluentAssertions` 8.9.0 — assertion library (aligned across all 3 test projects: UnitTests, IntegrationTests, E2ETests)
- `NSubstitute` — mocking (unit tests)
- `Testcontainers.MsSql` — real SQL Server 2022 in integration tests
- `Microsoft.AspNetCore.Mvc.Testing` — `WebApplicationFactory<Program>`
- `Microsoft.Playwright` — headless Chromium E2E tests
- `TngTech.ArchUnitNET` 0.13.3 — layer dependency rules, CQRS convention rules, sealedness checks (CLR namespace: `ArchUnitNET.*`)
- `TngTech.ArchUnitNET.xUnit` 0.13.1 — xUnit `.Check()` adapter for ArchUnitNET
- `NetArchTest.eNhancedEdition` 1.4.5 — folder-to-namespace alignment (`HaveSourceFilePathMatchingNamespace`)

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
- `Vitest` ^4.0.8 — unit test runner via `@angular/build:unit-test` (`npm test`)
- `@vitest/coverage-v8` ^4.1.5 — V8 coverage provider (80% line/branch/function thresholds in `angular.json`)
- `jsdom` ^28.0.0 — DOM environment for component tests
- Coverage thresholds: 80% statements/functions/lines, 75% branches (enforced in `angular.json > coverageThresholds`)

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

### Architecture Test Scripts
- `scripts/run-arch-tests.ps1` — PowerShell 7+ (cross-OS: Windows, Linux, macOS); runs `dotnet test tests/WebhookService.ArchitectureTests/`
- `scripts/run-arch-tests.sh` — Bash (Linux, macOS, Git Bash on Windows); same command

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
