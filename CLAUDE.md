# CLAUDE.md

Agent-facing guide for the Webhook Service repository. For full architecture, design decisions, and user guides see **README.md** (the single source of truth).

---

## Commands

### Backend (.NET 10)

```bash
# Build entire solution
dotnet build

# Run all tests
dotnet test

# Run a single test by name
dotnet test --filter "FullyQualifiedName~PostToWebhook_Returns200_AndCreatesRequest"

# Run only unit tests (no Docker required)
dotnet test tests/WebhookService.UnitTests/

# Run only integration tests (requires Docker — SQL Server container via Testcontainers)
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

# Start dev server (proxies /api, /webhook, /health to localhost:8080)
npm start

# Production build
npm run build

# Run unit tests
npm test
```

### E2E Tests (Playwright)

```bash
# Step 1 — build first; playwright.ps1 only exists after a successful build
dotnet build

# Step 2 — install Chromium (first run only)
pwsh tests/WebhookService.E2ETests/bin/Debug/net10.0/playwright.ps1 install

# Step 3 — run E2E tests (stack must be running; use port 8088 for docker compose)
E2E_BASE_URL=http://localhost:8088 dotnet test tests/WebhookService.E2ETests/

# Dev mode: Angular dev server at 4200 + backend at 8080
E2E_BASE_URL=http://localhost:4200 dotnet test tests/WebhookService.E2ETests/
```

### Docker

```bash
# Start full stack (copy .env.example to .env on first run)
cp .env.example .env
docker compose up -d

# Dev override (adds CORS for Angular dev server at :4200)
docker compose -f docker-compose.yml -f docker-compose.override.yml up -d

# Rebuild a single service after code change
docker compose build api && docker compose up -d api

# Tail logs for a service
docker compose logs -f api
```

---

## Architecture

Clean Architecture — dependencies flow strictly inward:

```
API → Application → Domain
Infrastructure → Application → Domain
```

```
src/
  WebhookService.Domain/          # Entities, value objects, repository interfaces, ISseNotifier
  WebhookService.Application/     # CQRS handlers (MediatR), DTOs, validation behaviors
  WebhookService.Infrastructure/  # EF Core (MSSQL), SseNotifier, RetentionCleanupService
  WebhookService.API/             # ASP.NET Core endpoints, Swagger, GlobalExceptionMiddleware
frontend/webhook-spa/             # Angular 21 standalone components, Angular Material, SSE client
tests/
  WebhookService.UnitTests/       # xUnit — domain entity tests, no infrastructure
  WebhookService.IntegrationTests/# xUnit + Testcontainers.MsSql + WebApplicationFactory
  WebhookService.E2ETests/        # Playwright headless Chromium
docker/
  sqlserver/                      # Custom image: entrypoint.sh polls → runs init.sql
  frontend/                       # Nginx multi-stage Dockerfile + nginx.conf
```

---

## Key Non-Obvious Facts

**URL routing:** The webhook *receiver* endpoint is `POST /webhook/{token:guid}`. The `webhookUrl` stored in the DB is `{WEBHOOK_BASE_URL}/webhook/{guid}`.

**SSE endpoint:** `GET /api/tokens/{tokenId}/sse` — not `/api/events/`. Max 10 concurrent connections per token. Response starts with `retry: 5000\n\n`. **Wire event name is `event: request`** (not `new-request`) — Angular `SseService` calls `es.addEventListener('request', ...)` and maps internally to `{ eventType: 'new-request' }`. The `onopen` handler emits `{ eventType: 'connected' }` so the green dot appears immediately on connect.

**Retention cleanup:** `RetentionCleanupService` is a `BackgroundService` running on a 24-hour `PeriodicTimer` with `IServiceScopeFactory`. Hangfire is not used. DB errors are caught and logged without stopping the timer.

**Token cache:** `GetOrCreateAsync` with 5-minute sliding expiration. **Null results are never cached** — cache entry is explicitly removed when token is not found or inactive. `SetCustomResponse`, `ResetCustomResponse`, and `DeleteToken` all call `cache.Remove(tokenId)` after mutating.

**Repository reads:** Both `WebhookTokenRepository` and `WebhookRequestRepository` use `.AsNoTracking()` on every SELECT query.

**IDOR protection:** `GetRequestById`, `ExportRequest`, and `DeleteRequest` all include `WHERE TokenId = @tokenId` to verify ownership.

**HTTP 422 validation:** `GlobalExceptionMiddleware` maps `FluentValidation.ValidationException` → HTTP 422 with field/message error list. Not 400.

**SSE disconnect handling:** `GlobalExceptionMiddleware` silently swallows `OperationCanceledException` when `context.RequestAborted.IsCancellationRequested`. `WriteErrorAsync` guards with `if (context.Response.HasStarted) return;` — SSE responses have already flushed headers; writing `StatusCode` on a started response throws `InvalidOperationException`.

**Custom response headers contract:** `SetCustomResponseRequest.Headers` (C#) and `SetCustomResponseDto.headers` (Angular) are `string` — a raw JSON string like `"{\"X-Foo\":\"bar\"}"` — **not** a `Record<string,string>`. The Angular dialog validates with `JSON.parse` but passes the raw string.

**Integration tests need Docker:** `WebApplicationFactory` spins up a real SQL Server 2022 container via Testcontainers. Tests stub `ISseNotifier` with a local `TestNullSseNotifier`.

**PowerShell encoding:** Use `[System.IO.File]::ReadAllText/WriteAllText` with `System.Text.Encoding.UTF8`. Never use bare `Get-Content`/`Set-Content` on UTF-8 files — they silently corrupt non-ASCII.

**ForwardedHeaders:** Nginx sets `X-Forwarded-For`; the API reads it via `ForwardedHeaders` middleware to capture the real client IP in `WebhookRequest.IpAddress`.

---

## Docker Compose Services

| Service | Dockerfile | Host Port |
|---------|-----------|-----------|
| `api` | `./Dockerfile` (repo root, context `.`) | 8080 |
| `frontend` | `docker/frontend/Dockerfile` (context `.`) | 8088 |
| `sqlserver` | `docker/sqlserver/Dockerfile` | 1433 |
| `seq` | `datalust/seq:latest` (no build) | 5341 (ingest, localhost only), 5342 (UI, localhost only) |

Nginx at 8088 reverse-proxies `/api/`, `/webhook/`, `/health` to `api:8080`. SSE routes (`~ ^/api/tokens/[^/]+/sse$`) use `proxy_buffering off; proxy_read_timeout 3600s`. SEQ ports are bound to `127.0.0.1` only. SEQ UI: `http://localhost:5342`.

---

## Key File Map

Files where knowing their location saves substantial search time:

| File | What it does |
|------|-------------|
| `src/WebhookService.API/Program.cs` | DI registration, middleware pipeline, endpoint mapping |
| `src/WebhookService.API/Middleware/GlobalExceptionMiddleware.cs` | Exception → HTTP status; SSE disconnect guard; `HasStarted` check |
| `src/WebhookService.API/Controllers/WebhookController.cs` | `POST /webhook/{token:guid}` receiver — captures and persists requests |
| `src/WebhookService.API/Controllers/SseController.cs` | `GET /api/tokens/{id}/sse` — SSE channel subscribe/stream |
| `src/WebhookService.API/Controllers/TokensController.cs` | Token CRUD + `SetCustomResponse` / `ResetCustomResponse` |
| `src/WebhookService.API/Controllers/RequestsController.cs` | Request paging, GetById, Export (JSON file), ClearAll, Delete |
| `src/WebhookService.API/Options/WebhookOptions.cs` | Options model: `BaseUrl`, `RetentionDays`, `MaxRequestSizeMb` |
| `src/WebhookService.API/Options/WebhookOptionsValidator.cs` | Startup-time `IValidateOptions` — throws if config is invalid |
| `src/WebhookService.Domain/Entities/WebhookToken.cs` | Token aggregate root; owns `CustomResponse` value object |
| `src/WebhookService.Domain/Entities/WebhookRequest.cs` | Request entity; headers stored as JSON string |
| `src/WebhookService.Domain/ValueObjects/CustomResponse.cs` | Owned entity (EF Core): `StatusCode`, `ContentType`, `Body`, `Headers` |
| `src/WebhookService.Domain/Services/ISseNotifier.cs` | SSE notifier interface — `SseNotifier` (prod) / `TestNullSseNotifier` (tests) |
| `src/WebhookService.Application/Common/Behaviors/ValidationBehavior.cs` | MediatR pipeline — runs FluentValidation before every handler |
| `src/WebhookService.Application/Common/Behaviors/LoggingBehavior.cs` | MediatR pipeline — logs request/response with timing |
| `src/WebhookService.Infrastructure/Persistence/ApplicationDbContext.cs` | EF Core DbContext; entity configurations via `IEntityTypeConfiguration<T>` |
| `src/WebhookService.Infrastructure/Sse/SseNotifier.cs` | `ConcurrentDictionary` of `Channel<SseEvent>` per token; `TryWrite` O(1) |
| `src/WebhookService.Infrastructure/BackgroundServices/RetentionCleanupService.cs` | `PeriodicTimer` (24h) cleanup; `IServiceScopeFactory` for scoped DbContext |
| `docker/sqlserver/entrypoint.sh` | Polls until SQL Server is ready, then runs `init.sql` |
| `docker/frontend/nginx.conf` | Reverse proxy config; `proxy_buffering off` for SSE |
| `frontend/webhook-spa/src/app/services/sse.service.ts` | Angular SSE client; maps wire `event: request` → `eventType: 'new-request'` |

---

## Feature Recipe: Adding a CQRS Command

Follow this pattern for any new write operation (e.g., `ArchiveToken`):

**1. Command record** — `src/WebhookService.Application/Tokens/Commands/ArchiveToken/ArchiveTokenCommand.cs`
```csharp
public sealed record ArchiveTokenCommand(Guid TokenId) : IRequest;
```

**2. Handler** — `ArchiveTokenCommandHandler.cs` in the same folder
```csharp
public sealed class ArchiveTokenCommandHandler(IWebhookTokenRepository repo)
    : IRequestHandler<ArchiveTokenCommand>
{
    public async Task Handle(ArchiveTokenCommand cmd, CancellationToken ct)
    {
        var token = await repo.FindByIdAsync(cmd.TokenId, ct)
            ?? throw new NotFoundException(nameof(WebhookToken), cmd.TokenId);
        // mutate and persist
        await repo.UpdateAsync(token, ct);
    }
}
```

**3. Validator (if inputs need checking)** — `ArchiveTokenCommandValidator.cs`
```csharp
public sealed class ArchiveTokenCommandValidator : AbstractValidator<ArchiveTokenCommand>
{
    public ArchiveTokenCommandValidator()
    {
        RuleFor(x => x.TokenId).NotEmpty();
    }
}
```
`ValidationBehavior` auto-discovers validators in the Application assembly — no manual registration.

**4. Controller method** — add to `TokensController.cs`
```csharp
[HttpPost("{id}/archive")]
public async Task<IActionResult> Archive(Guid id)
{
    await mediator.Send(new ArchiveTokenCommand(id));
    return NoContent();
}
```

**5. If mutating cached token data:** Call `cache.Remove(cmd.TokenId)` in the handler. Skipping this serves stale data for up to 5 minutes.

For queries: implement `IRequest<TResult>` and return `TResult` from the handler. Validators are optional for read-only queries.

---

## Testing Quick-Ref

| Scenario | Command |
|----------|---------|
| Quick smoke — domain logic only | `dotnet test tests/WebhookService.UnitTests/` |
| Full backend (requires Docker) | `dotnet test` |
| Single test by name | `dotnet test --filter "FullyQualifiedName~<MethodName>"` |
| E2E against docker compose stack | `E2E_BASE_URL=http://localhost:8088 E2E_AUTH_PASSWORD=admin dotnet test tests/WebhookService.E2ETests/` |
| Angular unit tests | `cd frontend/webhook-spa && npm test` |

**Gotchas:**
- Integration tests **fail silently or hang** if Docker Desktop is not running — the Testcontainers SQL Server container never starts.
- `playwright.ps1` does not exist until after `dotnet build`. Always build before the install step.
- E2E tests require the full stack to be running; they are not self-contained.
- `TestNullSseNotifier` in integration tests is a no-op — SSE delivery is not exercised by the integration suite.
- `--no-build` skips compilation. Only use it when you are certain no code has changed.

---

## DANGER ZONE — Invariants Never to Change Without Coordinating Both Sides

| Invariant | Why it must not change unilaterally |
|-----------|-------------------------------------|
| SSE wire event name `event: request` | Angular `SseService` hardcodes `addEventListener('request', ...)`. Rename the server side → Angular stops receiving events with no error. |
| `Headers` field type `string` (raw JSON) on both C# and Angular | The dialog validates with `JSON.parse` but sends the raw string. Changing to `object` on one side → 400 or silent empty headers. |
| `cache.Remove(tokenId)` on every token mutation | Omitting it → stale custom response or active-state served for up to 5 minutes. |
| `if (context.Response.HasStarted) return;` guard in `GlobalExceptionMiddleware` | SSE responses already flushed headers. Without this guard, setting `StatusCode` throws `InvalidOperationException`. |
| Silent swallow of `OperationCanceledException` when `RequestAborted.IsCancellationRequested` | Normal SSE client disconnect. Logging it as an error floods SEQ on every tab close. |
| `.AsNoTracking()` on all repository reads | Removing it enables EF Core change tracking on read-only queries; mutations in the same scope may unexpectedly persist phantom changes. |
| `WHERE TokenId = @tokenId` in GetRequestById / ExportRequest / DeleteRequest | Removing it enables IDOR — any token holder can read or delete another token's requests. **Security regression.** |
| Domain project has zero references to Application / Infrastructure / API | Violating this collapses the Clean Architecture dependency rule; enforced by project reference graph. |
| `[AllowAnonymous]` on `WebhookController` | External callers never have credentials. Removing it breaks all webhook delivery. |
| `[AllowAnonymous]` on `AuthController` | Login endpoint must be reachable before a session exists. Removing it creates an unbreakable auth loop. |
| `.AllowAnonymous()` on both `MapHealthChecks` calls | Global fallback policy applies to minimal API endpoints too. Without this, Docker health checks return 401 and the container never becomes healthy. |
| `OnRedirectToLogin` returns `401`, not `302` | SPA needs to intercept the 401 and navigate client-side. A `302` redirects the browser before Angular can handle it, breaking the login flow. |
| `UseAuthentication()` before `UseAuthorization()` in middleware | ASP.NET Core enforces this order; swapping it silently breaks all authorization checks. |
| `UseAuthentication()` after `UseForwardedHeaders()` | `ForwardedHeaders` must resolve the real proto first so `SameAsRequest` sets the `Secure` cookie flag correctly behind Nginx. |
| `checkSession()` swallows errors in `APP_INITIALIZER` | A throw inside `APP_INITIALIZER` blocks Angular startup entirely, leaving a blank screen. |
| Interceptor excludes `/api/auth/` from the 401→`/login` redirect | `POST /api/auth/login` returns 401 on bad credentials. Redirecting to `/login` on that 401 creates an infinite redirect loop in the login form. |
| `EventSource({ withCredentials: true })` in `SseService` | Dev mode is cross-origin (`:4200` → `:8080`). Without `withCredentials`, the browser blocks the auth cookie and SSE gets 401 silently. |
| `.AllowCredentials()` in the dev CORS policy | Required for cross-origin cookie delivery in dev mode. Without it, the browser discards the `Set-Cookie` header and login appears to succeed but leaves no session. |
| `AUTH_PASSWORD_HASH` is a BCrypt hash, never plaintext | The validator checks the `$2` prefix at startup. Plaintext fails validation and the app refuses to start. |
| `[mat-dialog-close]="null"` (with binding) on the Cancel button in `CreateTokenDialogComponent` | `mat-dialog-close` without a value binding closes with `""` (empty string), not `undefined`. The dashboard guard uses `== null`, so `""` bypasses it and silently creates a no-description token. |

---

## Common Failures & Regression Risks

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| Integration tests hang / time out | Docker Desktop not running | Start Docker, retry |
| `playwright.ps1 not found` | E2E project not built yet | `dotnet build` first |
| SSE green dot never appears | `onopen` handler removed or `eventType: 'connected'` not emitted | Restore `onopen` in `SseService` |
| Custom response returns 422 | `Headers` sent as parsed object instead of JSON string | Send `JSON.stringify(headers)` as the `headers` field |
| Webhook URLs contain wrong base URL | `WEBHOOK_BASE_URL` env var wrong in `.env` | Set `WEBHOOK_BASE_URL=http://localhost:8088` |
| SQL Server container exits immediately | Wrong `SA_PASSWORD` (must meet complexity) or < 3 GB RAM | Check `.env`, free RAM |
| `dotnet ef database update` fails in CI | `dotnet-ef` tool not installed | `dotnet tool install -g dotnet-ef` |
| 404 on all `/api/*` routes in docker compose | API not healthy; nginx started before API was ready | `docker compose logs api`, wait for health check |
| Angular proxy 404 during `npm start` | Backend not running on port 8080 | `dotnet run --project src/WebhookService.API` first |
| New command handler never invoked | Validator constructor throws, or handler in wrong assembly | Verify validator has at least one `RuleFor`; handler must be in Application project |
| EF migration fails with "pending model changes" | Model changed without adding a migration | `dotnet ef migrations add <Name> --project src/WebhookService.Infrastructure --startup-project src/WebhookService.API` |
| API exits at startup with `ValidateOptionsResult.Fail` | `AUTH_USERNAME` or `AUTH_PASSWORD_HASH` missing or hash lacks `$2` prefix | Set both vars in `.env`; regenerate hash with `python3 -c "import bcrypt; print(bcrypt.hashpw(b'pass', bcrypt.gensalt(12)).decode())"` |
| App always redirects to `/login` even after correct credentials | `AUTH_PASSWORD_HASH` is plaintext, not a BCrypt hash | Replace with BCrypt hash; see §10 Configuration Reference in README |
| SSE 401 in dev mode (`:4200` → `:8080`) | `withCredentials` removed from `EventSource` call in `SseService` | Restore `{ withCredentials: true }` |
| Login succeeds but session not retained (dev cross-origin) | `.AllowCredentials()` removed from dev CORS policy | Add `.AllowCredentials()` to the CORS policy in `Program.cs` |
| Health checks return 401 | `.AllowAnonymous()` removed from `MapHealthChecks` | Restore `.AllowAnonymous()` on both health check endpoints |
| Clicking Cancel on "New Webhook URL" dialog creates a token | `mat-dialog-close` attribute without binding changed to `[mat-dialog-close]="null"` (reverted), or dashboard guard weakened from `== null` back to `=== undefined` | Restore `[mat-dialog-close]="null"` in `CreateTokenDialogComponent` and `== null` guard in `DashboardComponent.openCreate()` |

---

## README Cross-Reference

| Topic | README Section |
|-------|---------------|
| System requirements | §2 System Requirements |
| Complete API contract (all endpoints, status codes) | §7.3 API Contract |
| Step-by-step data flows (receive, SSE, custom response) | §6 Data Flows |
| CQRS handler map (all commands and queries) | §7.5 Application Layer — CQRS Map |
| SSE architecture (channel diagram, backpressure) | §7.4 SSE Architecture |
| Token cache strategy (hit/miss/invalidate) | §7.7 Token Cache Strategy |
| Docker Compose service table + nginx config | §9 Docker Compose |
| All environment variables | §10 Configuration Reference |
| Security model and deployment hardening | §11 Security Model |
| Adding EF migrations | §12 Development Guide |
| E2E test setup (step-by-step) | §13 Testing |
| SEQ log events and useful queries | §14 Observability |
| Commit format + PR checklist | §15 Contributing |
| Design decisions and trade-off rationale | §16 Design Decisions, §17 Trade-offs |
| Troubleshooting (SQL Server, SSE, ports, etc.) | §20 Troubleshooting |
