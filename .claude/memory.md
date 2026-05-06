---
name: Webhook Service Project Memory
description: Durable project context for the webhook-service repository
type: project
---

# Webhook Service — Project Memory

## What This Project Is

A self-hosted webhook inspection tool: receive arbitrary HTTP requests at a unique URL, inspect headers/body/metadata in a real-time dashboard, and optionally serve custom HTTP responses.

## Tech Stack

- **Backend:** ASP.NET Core (.NET 10), Clean Architecture, CQRS via MediatR, EF Core + SQL Server
- **Frontend:** Angular 21 standalone components, Angular Material, SSE for live updates
- **Tests:** xUnit (unit + integration via Testcontainers), Playwright (E2E)
- **Infra:** Docker Compose (api, frontend/nginx, sqlserver, seq)

## Key Architectural Facts

- SSE wire event name is `event: request` — Angular listens via `addEventListener('request', ...)`.
- `Headers` field on custom response is a raw JSON string (`string`), not an object, on both C# and Angular sides.
- Auth uses BCrypt hash in `AUTH_PASSWORD_HASH` env var — plaintext fails startup validation.
- Token cache has 60-second sliding expiration; all mutations call `cache.Remove(tokenId)`.
- All repository reads use `.AsNoTracking()`.
- IDOR protection: `GetRequestById`, `ExportRequest`, `DeleteRequest` all include `WHERE TokenId = @tokenId`.

## Running Tests

```bash
# Unit tests only (no Docker)
dotnet test tests/WebhookService.UnitTests/

# All tests (Docker required for integration)
dotnet test

# E2E (stack must be running)
E2E_BASE_URL=http://localhost:8088 E2E_AUTH_PASSWORD=admin dotnet test tests/WebhookService.E2ETests/
```

## Docker Compose Ports

| Service    | Port  |
|------------|-------|
| API        | 8080  |
| Frontend   | 8088  |
| SQL Server | 1433  |
| SEQ UI     | 5342  |

## Known Invariants (Never Break Unilaterally)

See CLAUDE.md §DANGER ZONE for the full list. Critical ones:
- `event: request` wire name must match Angular `addEventListener('request', ...)`
- `cache.Remove(tokenId)` on every token mutation
- `if (context.Response.HasStarted) return;` guard in GlobalExceptionMiddleware
- `[AllowAnonymous]` on WebhookController and AuthController
