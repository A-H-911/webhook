<!-- Generated: 2026-05-07 | Updated: HashGen tool (renamed from RotatePassword), rate limiting, antiforgery, session revocation -->

# Dependencies

## Backend (.NET 10)

### Core Framework
- `Microsoft.AspNetCore` — ASP.NET Core 10 Web API
- `Microsoft.EntityFrameworkCore.SqlServer` — ORM + SQL Server provider
- `Microsoft.EntityFrameworkCore.Design` — EF migrations tooling

### CQRS / Validation
- `MediatR` — CQRS command/query bus + pipeline behaviors
- `FluentValidation.AspNetCore` — request validation (auto-discovered in Application assembly)

### Auth
- `Microsoft.AspNetCore.Authentication.Cookies` — cookie-based session auth
- `BCrypt.Net-Next` — password hash verification at login

### Resilience & Security (Updated 2026-05-07)
- `Polly` — retry on startup DB migration (5 attempts, exponential backoff)
- `Microsoft.AspNetCore.RateLimiting` — fixed-window rate limiter (login 5/min, webhook-receiver configurable)
- `Microsoft.AspNetCore.Antiforgery` — CSRF protection, X-XSRF-TOKEN header validation

### Observability
- `Serilog.AspNetCore` — structured logging
- `Serilog.Sinks.Seq` — log shipping to SEQ
- `Microsoft.AspNetCore.Diagnostics.HealthChecks` — /health/live and /health/ready
- `AspNetCore.HealthChecks.SqlServer` — SQL Server ping for /health/ready

### Testing
- `xUnit` — test framework (unit + integration + E2E)
- `Testcontainers.MsSql` — real SQL Server 2022 container in integration tests
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

| Service     | Image                      | Host Port             | Purpose                          |
|-------------|----------------------------|-----------------------|----------------------------------|
| `sqlserver` | Custom (SQL Server 2022)   | 1433                  | Primary data store               |
| `seq`       | `datalust/seq:latest`      | 5341 (ingest), 5342 (UI) | Structured log aggregation    |
| `frontend`  | Custom nginx               | 8088                  | Static SPA + reverse proxy       |
| `api`       | Custom .NET                | 8080                  | Backend API                      |

## Tools

### HashGen (Renamed from RotatePassword, 2026-05-07)
- CLI tool for generating BCrypt password hashes
- Usage: `dotnet run --project tools/HashGen -- --password 'mypassword'`
- Output: BCrypt hash (starts with `$2`)
- Used to generate `AUTH_PASSWORD_HASH` for `.env` file
- Never commit raw passwords; only store BCrypt hashes

## Key Config / Env
```
WEBHOOK_BASE_URL          — public base URL (e.g. http://localhost:8088)
AUTH_USERNAME             — single admin username
AUTH_PASSWORD_HASH        — BCrypt hash (generate via HashGen tool, starts with $2)
CORS_ALLOWED_ORIGINS      — comma-separated (dev: http://localhost:4200)
Webhook:ReceiverRateLimitPerSecond — receiver rate limit (default 5/sec)
Webhook:RetentionDays     — request retention (default 7)
ConnectionStrings__WebhookDb — MSSQL connection string
SEQ_URL                   — Seq ingest endpoint (optional, localhost only)
```
