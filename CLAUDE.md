# CLAUDE.md

Agent-facing guide for the Hookbin repository. For full architecture, design decisions, and user guides see **README.md** (the single source of truth).

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
dotnet test tests/Hookbin.UnitTests/

# Run architecture tests (no Docker required)
dotnet test tests/Hookbin.ArchitectureTests/
# OR via cross-OS scripts:
#   pwsh scripts/run-arch-tests.ps1      (Windows / Linux / macOS — PowerShell 7+)
#   bash scripts/run-arch-tests.sh       (Linux / macOS / Git Bash on Windows)

# Run only integration tests (requires Docker — SQL Server container via Testcontainers)
dotnet test tests/Hookbin.IntegrationTests/

# Apply EF Core migrations
dotnet ef database update --project src/Hookbin.Infrastructure --startup-project src/Hookbin.API

# Format code
dotnet format
```

### Frontend (Angular 21)

```bash
cd frontend/hookbin-spa

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
pwsh tests/Hookbin.E2ETests/bin/Debug/net10.0/playwright.ps1 install

# Step 3 — run E2E tests (stack must be running; use port 8088 for docker compose)
E2E_BASE_URL=http://localhost:8088 E2E_AUTH_PASSWORD=Admin123! dotnet test tests/Hookbin.E2ETests/

# Dev mode: Angular dev server at 4200 + backend at 8080
E2E_BASE_URL=http://localhost:4200 E2E_AUTH_PASSWORD=Admin123! dotnet test tests/Hookbin.E2ETests/
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

Three deployable units share the same Domain/Application/Infrastructure libraries, SQL DB, and Redis:

```
src/
  Hookbin.Domain/          # Entities, value objects, repository interfaces, ISseNotifier
  Hookbin.Application/     # CQRS handlers (MediatR), DTOs, validation behaviors
  Hookbin.Infrastructure/  # EF Core (MSSQL), SseNotifier, RedisSseBridgeService, stream consumer, retention
  Hookbin.API/             # ASP.NET Core HTTP endpoints — AddCoreInfrastructure + AddApiInfrastructure
  Hookbin.StreamWorker/    # Redis stream consumer worker — AddCoreInfrastructure + AddStreamWorkerInfrastructure
  Hookbin.JobsWorker/      # Retention cleanup worker — AddCoreInfrastructure + AddJobsWorkerInfrastructure
frontend/hookbin-spa/             # Angular 21 standalone components, Angular Material, SSE client
tests/
  Hookbin.UnitTests/       # xUnit — domain entity tests, no infrastructure
  Hookbin.IntegrationTests/# xUnit + Testcontainers.MsSql + WebApplicationFactory
  Hookbin.E2ETests/        # Playwright headless Chromium
  Hookbin.ArchitectureTests/ # ArchUnitNET + NetArchTest — layer deps, CQRS conventions, no Docker
docker/
  sqlserver/                      # Custom image: entrypoint.sh polls → runs init.sql
  frontend/                       # Nginx multi-stage Dockerfile + nginx.conf
```

---

## Key Non-Obvious Facts

**URL routing:** The webhook *receiver* endpoint is `POST /webhook/{token:guid}`. The `webhookUrl` stored in the DB is `{HOOKBIN_BASE_URL}/webhook/{guid}`.

**SSE endpoint:** `GET /api/tokens/{tokenId}/sse` — not `/api/events/`. Max 10 concurrent connections per token. Response starts with `retry: 5000\n\n`. **Wire event name is `event: request`** (not `new-request`) — Angular `SseService` calls `es.addEventListener('request', ...)` and maps internally to `{ eventType: 'new-request' }`. The `onopen` handler emits `{ eventType: 'connected' }` so the green dot appears immediately on connect.

**Retention cleanup:** `RetentionCleanupService` is a `BackgroundService` running on a 24-hour `PeriodicTimer` with `IServiceScopeFactory`. It runs in the **`Hookbin.JobsWorker`** process (not the API). Hangfire is not used. DB errors are caught and logged without stopping the timer. `jobs-worker` must run as **single replica only** — no leader election exists.

**Token cache:** `ITokenCache` port (`src/Hookbin.Application/Caching/ITokenCache.cs`) backed by **Redis** via `RedisTokenCache` (`src/Hookbin.Infrastructure/Redis/RedisTokenCache.cs`). Key `wh:token:{guid}`, JSON-serialized `WebhookToken`, 5-minute **sliding** TTL (`KeyExpireAsync` extends on read). **Fail-open** — `RedisException`/`RedisTimeoutException` are caught and logged; handlers fall back to the database. **Null results are never cached.** Both active and inactive tokens are cached (receiver needs fast 410 lookup). `SetCustomResponse`, `ResetCustomResponse`, `DeleteToken`, and `UpdateToken` all call `tokenCache.RemoveAsync(token.Token, ct)` after mutating.

**Inactive token audit trail:** Receiver path uses `GetByTokenIncludingInactiveAsync` (not filtered by `IsActive`) and **always** persists requests from inactive tokens. Returns 410 Gone but request is recorded in DB for compliance/audit purposes. Note: this **deactivate** path (setting `IsActive=false` via `UpdateToken`) is distinct from the **delete** path (`DeleteTokenCommand`), which hard-deletes both the token row and all its `WebhookRequest` rows via EF Core cascade.

**Hard-delete on token deletion:** `DeleteTokenCommandHandler` calls `repository.DeleteAsync(id)` which removes the `WebhookToken` row. EF Core cascade (`WebhookRequestConfiguration.cs:40`, `OnDelete(DeleteBehavior.Cascade)`) removes all child `WebhookRequest` rows in the same transaction. The dashboard metrics queries count `WebhookRequest` rows directly — leaving orphans behind would permanently inflate `requestsCapturedAllTime`, `requestsCapturedLast24h`, and `liveEndpoints`. The `Deactivate()` domain method is preserved for the `UpdateToken { isActive: false }` pause path.

**Repository reads:** Both `WebhookTokenRepository` and `WebhookRequestRepository` use `.AsNoTracking()` on every SELECT query. `WebhookRequestRepository` orders paginated results by `ReceivedAt DESC, THEN Id DESC` for deterministic pagination.

**IDOR protection:** `GetRequestById`, `ExportRequest`, and `DeleteRequest` all include `WHERE TokenId = @tokenId` to verify ownership.

**HTTP 422 validation:** `GlobalExceptionMiddleware` maps `FluentValidation.ValidationException` → HTTP 422 with field/message error list. Not 400.

**Bad request handling:** `GlobalExceptionMiddleware` catches `BadHttpRequestException` (from body read errors or Kestrel validation) and returns 400 or 413 (per `ex.StatusCode`). Logged with method/path context. Example: `IOException` on body read → `BadHttpRequestException` (400).

**SSE disconnect handling:** `GlobalExceptionMiddleware` silently swallows `OperationCanceledException` when `context.RequestAborted.IsCancellationRequested`. `WriteErrorAsync` guards with `if (context.Response.HasStarted) return;` — SSE responses have already flushed headers; writing `StatusCode` on a started response throws `InvalidOperationException`.

**Custom response headers contract:** `SetCustomResponseRequest.Headers` (C#) and `SetCustomResponseDto.headers` (Angular) are `string` — a raw JSON string like `"{\"X-Foo\":\"bar\"}"` — **not** a `Record<string,string>`. The Angular dialog validates with `JSON.parse` but passes the raw string.

**Integration tests need Docker:** `WebApplicationFactory` spins up a real SQL Server 2022 container via Testcontainers. Tests stub `ISseNotifier` with a local `TestNullSseNotifier`.

**PowerShell encoding:** Use `[System.IO.File]::ReadAllText/WriteAllText` with `System.Text.Encoding.UTF8`. Never use bare `Get-Content`/`Set-Content` on UTF-8 files — they silently corrupt non-ASCII.

**ForwardedHeaders:** Nginx sets `X-Forwarded-For`; the API reads it via `ForwardedHeaders` middleware to capture the real client IP in `WebhookRequest.IpAddress`.

**Session revocation:** Logout calls `RedisSessionRevocationStore.RevokeAsync(sid, ttl)` writing `wh:revoked-session:{sid}` to Redis. `OnValidatePrincipal` reads that key on every authenticated request and rejects revoked sessions with 401. Fail-open on Redis outage (so an outage doesn't lock the admin out). Cross-instance by construction.

**Rate limiting:** Two `UseRateLimiter` partitions — webhook receiver (default 250/sec per `{token:guid}`) and login (5/min per real client IP). Both rely on `UseForwardedHeaders` resolving the real IP first.

**Antiforgery:** `o.HeaderName = "X-XSRF-TOKEN"`; middleware at `Program.cs:199-209` emits the `XSRF-TOKEN` cookie automatically on authenticated requests. Angular `HttpClient` round-trips it. Header rename or missing cookie = 400 on every state-changing request.

**GeoIP enrichment:** `WebhookRequest.IpCountry` is populated by `WebhookController.Receive` on the API hot path (`WebhookController.cs:37`) via `IGeoIpService.GetCountry(ipAddress)` (MaxMind GeoLite2) — **not** by the StreamWorker. Enrichment happens before the XADD so the country tag travels through the stream payload. Falls back to `null` for private IPs and lookup misses.

---

## Docker Compose Services

| Service | Dockerfile / args | Host Port |
|---------|-----------|-----------|
| `api` | `./Dockerfile` `PROJECT_NAME=Hookbin.API` | 8080 |
| `stream-worker` | `./Dockerfile` `PROJECT_NAME=Hookbin.StreamWorker` | none (internal only) |
| `jobs-worker` | `./Dockerfile` `PROJECT_NAME=Hookbin.JobsWorker` (single replica) | none (internal only) |
| `frontend` | `docker/frontend/Dockerfile` (context `.`) | 8088 |
| `sqlserver` | `docker/sqlserver/Dockerfile` | 1433 |
| `seq` | `datalust/seq:latest` (no build) | 5341 (ingest, localhost only), 5342 (UI, localhost only) |

Nginx at 8088 reverse-proxies `/api/`, `/webhook/`, `/health` to `api:8080`. SSE routes (`~ ^/api/tokens/[^/]+/sse$`) use `proxy_buffering off; proxy_read_timeout 3600s`. SEQ ports are bound to `127.0.0.1` only. SEQ UI: `http://localhost:5342`.

---

## Key File Map

Files where knowing their location saves substantial search time:

| File | What it does |
|------|-------------|
| `src/Hookbin.API/Program.cs` | DI registration, middleware pipeline, endpoint mapping |
| `src/Hookbin.API/Middleware/GlobalExceptionMiddleware.cs` | Exception → HTTP status; SSE disconnect guard; `HasStarted` check |
| `src/Hookbin.API/Controllers/WebhookController.cs` | `POST /webhook/{token:guid}` receiver — captures and persists requests |
| `src/Hookbin.API/Controllers/SseController.cs` | `GET /api/tokens/{id}/sse` — SSE channel subscribe/stream |
| `src/Hookbin.API/Controllers/TokensController.cs` | Token CRUD + `SetCustomResponse` / `ResetCustomResponse` |
| `src/Hookbin.API/Controllers/RequestsController.cs` | Request paging, GetById, Export (JSON file), ClearAll, Delete |
| `src/Hookbin.API/Options/WebhookOptions.cs` | Options model: `BaseUrl`, `RetentionDays`, `MaxRequestSizeMb` |
| `src/Hookbin.API/Options/WebhookOptionsValidator.cs` | Startup-time `IValidateOptions` — throws if config is invalid |
| `src/Hookbin.Domain/Entities/WebhookToken.cs` | Token aggregate root; owns `CustomResponse` value object |
| `src/Hookbin.Domain/Entities/WebhookRequest.cs` | Request entity; headers stored as JSON string |
| `src/Hookbin.Domain/ValueObjects/CustomResponse.cs` | Owned entity (EF Core): `StatusCode`, `ContentType`, `Body`, `Headers` |
| `src/Hookbin.Domain/Services/ISseNotifier.cs` | SSE notifier interface — `SseNotifier` (prod) / `TestNullSseNotifier` (tests) |
| `src/Hookbin.Application/Common/Behaviors/ValidationBehavior.cs` | MediatR pipeline — runs FluentValidation before every handler |
| `src/Hookbin.Application/Common/Behaviors/LoggingBehavior.cs` | MediatR pipeline — logs request/response with timing |
| `src/Hookbin.Infrastructure/Persistence/ApplicationDbContext.cs` | EF Core DbContext; entity configurations via `IEntityTypeConfiguration<T>` |
| `src/Hookbin.Infrastructure/DependencyInjection.cs` | Four focused DI extensions: `AddCoreInfrastructure`, `AddApiInfrastructure`, `AddStreamWorkerInfrastructure`, `AddJobsWorkerInfrastructure` |
| `src/Hookbin.Infrastructure/Sse/SseNotifier.cs` | `ConcurrentDictionary` of `Channel<SseEvent>` per token; `TryWrite` O(1) |
| `src/Hookbin.Infrastructure/BackgroundServices/RetentionCleanupService.cs` | `PeriodicTimer` (24h) cleanup; `IServiceScopeFactory` for scoped DbContext |
| `src/Hookbin.StreamWorker/Program.cs` | StreamWorker entry point: `AddCoreInfrastructure + AddStreamWorkerInfrastructure`; polls DB readiness via Polly; `/health/live` + `/health/ready` |
| `src/Hookbin.JobsWorker/Program.cs` | JobsWorker entry point: `AddCoreInfrastructure + AddJobsWorkerInfrastructure`; polls DB readiness via Polly; `/health/live` + `/health/ready` (SQL-only) |
| `docker/sqlserver/entrypoint.sh` | Polls until SQL Server is ready, then runs `init.sql` |
| `docker/frontend/nginx.conf` | Reverse proxy config; `proxy_buffering off` for SSE; security headers (HSTS, CSP, Permissions-Policy) |
| `frontend/hookbin-spa/src/app/services/sse.service.ts` | Angular SSE client; maps wire `event: request` → `eventType: 'new-request'` |
| `tools/RotatePassword/Program.cs` | BCrypt password-rotation CLI — generates `AUTH_PASSWORD_HASH` for `.env` |

---

## Feature Recipe: Adding a CQRS Command

Follow this pattern for any new write operation (e.g., `ArchiveToken`):

**1. Command record** — `src/Hookbin.Application/Tokens/Commands/ArchiveToken/ArchiveTokenCommand.cs`
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

**5. If mutating cached token data:** Inject `ITokenCache` and call `await tokenCache.RemoveAsync(token.Token, cancellationToken)` in the handler. Skipping this serves stale data for up to 5 minutes (Redis sliding TTL). The cache backend is Redis (`wh:token:{guid}`) — invalidation is cross-instance.

**6. Architecture tests enforce conventions automatically.** The tests in `tests/Hookbin.ArchitectureTests/` will fail the CI `architecture-test` job if: the command is not a `sealed record`, the handler is not `internal sealed`, the validator does not inherit `AbstractValidator`, or the folder does not match the namespace. Run `dotnet test tests/Hookbin.ArchitectureTests/` locally before pushing.

For queries: implement `IRequest<TResult>` and return `TResult` from the handler. Validators are optional for read-only queries.

---

## Testing Quick-Ref

| Scenario | Command |
|----------|---------|
| Architecture rules (no Docker) | `dotnet test tests/Hookbin.ArchitectureTests/` |
| Quick smoke — domain logic only | `dotnet test tests/Hookbin.UnitTests/` |
| Full backend (requires Docker) | `dotnet test` |
| Single test by name | `dotnet test --filter "FullyQualifiedName~<MethodName>"` |
| E2E against docker compose stack | `E2E_BASE_URL=http://localhost:8088 E2E_AUTH_PASSWORD=Admin123! dotnet test tests/Hookbin.E2ETests/` |
| Angular unit tests | `cd frontend/hookbin-spa && npm test` |
| Angular unit tests with coverage | `cd frontend/hookbin-spa && npm test -- --watch=false --coverage` |
| Rebuild containers + wait healthy | `pwsh scripts/rebuild-and-wait.ps1` (Windows) or `bash scripts/rebuild-and-wait.sh` (Linux/macOS) |
| Rebuild single service | `pwsh scripts/rebuild-and-wait.ps1 -Services api` |

Backend coverage thresholds are enforced via `tests/coverlet.runsettings` (80% line/method). Pass `--settings tests/coverlet.runsettings` to `dotnet test` to apply them locally.

**Gotchas:**
- Integration tests **fail silently or hang** if Docker Desktop is not running — the Testcontainers SQL Server container never starts.
- `playwright.ps1` does not exist until after `dotnet build`. Always build before the install step.
- E2E tests require the full stack to be running; they are not self-contained.
- `TestNullSseNotifier` in integration tests is a no-op — SSE delivery is not exercised by the integration suite.
- `--no-build` skips compilation. Only use it when you are certain no code has changed.
- **Run `scripts/rebuild-and-wait` before E2E or integration tests after any backend/worker code change.** Nginx caches the API container IP on startup — the script reloads nginx after rebuild so the new container IP is picked up.

---

## DANGER ZONE — Invariants Never to Change Without Coordinating Both Sides

| Invariant | Why it must not change unilaterally |
|-----------|-------------------------------------|
| SSE wire event name `event: request` | Angular `SseService` hardcodes `addEventListener('request', ...)`. Rename the server side → Angular stops receiving events with no error. |
| `Headers` field type `string` (raw JSON) on both C# and Angular | The dialog validates with `JSON.parse` but sends the raw string. Changing to `object` on one side → 400 or silent empty headers. |
| `tokenCache.RemoveAsync(token.Token)` on every token mutation (`ITokenCache` → `RedisTokenCache`, key `wh:token:{guid}`) | Omitting it → stale custom response or active-state served for up to 5 minutes across every API instance (Redis is cross-instance). |
| `if (context.Response.HasStarted) return;` guard in `GlobalExceptionMiddleware` | SSE responses already flushed headers. Without this guard, setting `StatusCode` throws `InvalidOperationException`. |
| Silent swallow of `OperationCanceledException` when `RequestAborted.IsCancellationRequested` | Normal SSE client disconnect. Logging it as an error floods SEQ on every tab close. |
| `.AsNoTracking()` on all repository reads | Removing it enables EF Core change tracking on read-only queries; mutations in the same scope may unexpectedly persist phantom changes. |
| **Always** persist WebhookRequest, even from inactive tokens | This applies to the **deactivate** path (`isActive=false`). Callers rely on audit trail; removing persistence breaks compliance logging and makes 410 signal ambiguous. The **delete** path hard-deletes everything by design — see next row. |
| `DeleteTokenCommandHandler` must hard-delete (not soft-delete) | Dashboard metrics query `WebhookRequest` rows directly without joining on `IsActive`. Soft-delete leaves orphan rows that permanently inflate `requestsCapturedAllTime` and `liveEndpoints`. The UI dialog text says "This can't be undone" — soft-delete is a contract violation. Hard-delete + EF Core cascade is the correct implementation. |
| Receiver uses `GetByTokenIncludingInactiveAsync`, not `GetByTokenAsync` | Dashboard queries filter `IsActive=1` but receiver needs both states. Swapping methods causes inactive tokens to return 404 instead of 410. |
| `tokenId` parameter in GetRequestById / ExportRequest / DeleteRequest repository methods | C# repository methods always include `tokenId` as a query filter (parameterized EF Core), preventing cross-token access. This is **not** raw SQL `WHERE` enforcement — it is enforced in C# method signatures. Single-admin deployment by design — no per-user tenancy. Removing the `tokenId` filter enables IDOR. |
| Domain project has zero references to Application / Infrastructure / API | Violating this collapses the Clean Architecture dependency rule; enforced by project reference graph and `tests/Hookbin.ArchitectureTests/Layers/LayerDependencyTests.cs`. |
| Architecture test conventions (CQRS naming, namespace alignment, sealed/internal checks) | If you intentionally need to break a documented convention, update the corresponding rule in `tests/Hookbin.ArchitectureTests/` in the same PR — the test failure is the early-warning system. |
| `jobs-worker` runs as single replica only | `RetentionCleanupService` has no leader election. Running two replicas double-deletes rows on every 24h tick (benign but wasteful) and risks overlapping range scans causing deadlocks under high write load. Use `deploy.replicas: 1` in compose. |
| `stream-worker` uses `HOOKBIN_WORKER_ID` env var for consumer name | Docker container IDs change on every `docker run`. If the consumer name changes, the old PEL entries are permanently orphaned in Redis — orphaned messages are never automatically reclaimed. Set `HOOKBIN_WORKER_ID=stream-worker-1` in compose. |
| Workers poll `CanConnectAsync`, never call `MigrateAsync` | API is the sole migration runner. If workers call `MigrateAsync` concurrently, SQL Server may deadlock schema-lock operations on cold start. Workers wait on DB readiness; migration races are the API's responsibility. |
| `RedisSseBridgeService` stays in the API, not the StreamWorker | It writes into the in-process `SseNotifier` channel — those channels only exist in the API process where SSE HTTP connections are held. Moving it to the worker makes SSE fan-out a no-op. |
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
| `AUTH_PASSWORD_HASH` is a BCrypt hash, never plaintext | The validator checks the `$2` prefix at startup. Plaintext fails validation and the app refuses to start. Use `tools/RotatePassword/` to generate a new hash: `dotnet run --project tools/RotatePassword -- --password '<strong>'`. Never commit the hash to source control. |
| `Headers`, `Body`, `IpAddress` are persisted **unredacted** by design | Single-admin trust model — only one admin account exists. All captured data (including `Cookie` headers and vendor HMAC secrets) is visible to the admin in the UI, DB, and SEQ. This is intentional. Never add automatic redaction without explicit approval from the deployment owner. |
| `[mat-dialog-close]="null"` (with binding) on the Cancel button in `CreateTokenDialogComponent` | `mat-dialog-close` without a value binding closes with `""` (empty string), not `undefined`. The dashboard guard uses `== null`, so `""` bypasses it and silently creates a no-description token. |
| Redis cache and session-revocation paths are **fail-open**; Redis Streams ingest is **fail-loud** | `RedisTokenCache` and `RedisSessionRevocationStore` catch `RedisException`/`RedisTimeoutException` and return safe defaults (DB fallback / `IsRevoked=false`) — a Redis outage should not lock the admin out or 500 every read. `RedisStreamConsumerService` does NOT mask outages; webhook ingest stops until Redis is back. Adding fail-open to the stream path would silently drop captured requests — never do this. |
| `RedisSessionRevocationStore.RevokeAsync` is called on logout with TTL ≥ session cookie lifetime | If the revocation TTL is shorter than the cookie's sliding window, a revoked session's marker expires before the cookie does, and the user is silently re-authenticated. Always pass `Auth__SessionHours` (matching the cookie). |
| Antiforgery `HeaderName = "X-XSRF-TOKEN"` and the cookie-emit middleware at `Program.cs:199-209` | The header name is the contract Angular `HttpClient` automatically round-trips from the `XSRF-TOKEN` cookie. Changing the name on the server breaks every state-changing API call from the SPA. Removing the cookie-emit middleware → no cookie → no header → 400 on every PUT/POST/DELETE. |
| Two `UseRateLimiter` policies — `"webhook-receiver"` (token-bucket partitioned by `{token:guid}` route value, default 250/sec) and `"login"` (fixed window, **global, no partition**, 5/min) | Per-token bucket isolates noisy producers without affecting unrelated tokens. The login policy is intentionally **global** (not per-IP) because under the single-admin model an attacker rotating IPs would defeat per-IP partitioning; the trade-off is that a brute-force attempt may briefly block the legitimate operator. Don't introduce a per-IP partition without re-evaluating that trade-off. |
| `ITokenCache` is the abstraction; `RedisTokenCache` is the only production implementation. Never bind `IMemoryCache` to token caching | Mixing the two would mean some instances cache locally and never see cross-instance invalidations — stale custom responses returned for up to 5 minutes per affected instance. If you need a swappable cache, swap the `ITokenCache` implementation in DI, not the consumer. |

---

## Common Failures & Regression Risks

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| Integration tests hang / time out | Docker Desktop not running | Start Docker, retry |
| `playwright.ps1 not found` | E2E project not built yet | `dotnet build` first |
| SSE green dot never appears | `onopen` handler removed or `eventType: 'connected'` not emitted | Restore `onopen` in `SseService` |
| Custom response returns 422 | `Headers` sent as parsed object instead of JSON string | Send `JSON.stringify(headers)` as the `headers` field |
| Webhook URLs contain wrong base URL | `HOOKBIN_BASE_URL` env var wrong in `.env` | Set `HOOKBIN_BASE_URL=http://localhost:8088` |
| SQL Server container exits immediately | Wrong `SA_PASSWORD` (must meet complexity) or < 3 GB RAM | Check `.env`, free RAM |
| `dotnet ef database update` fails in CI | `dotnet-ef` tool not installed | `dotnet tool install -g dotnet-ef` |
| 404 on all `/api/*` routes in docker compose | API not healthy; nginx started before API was ready | `docker compose logs api`, wait for health check |
| Angular proxy 404 during `npm start` | Backend not running on port 8080 | `dotnet run --project src/Hookbin.API` first |
| New command handler never invoked | Validator constructor throws, or handler in wrong assembly | Verify validator has at least one `RuleFor`; handler must be in Application project |
| EF migration fails with "pending model changes" | Model changed without adding a migration | `dotnet ef migrations add <Name> --project src/Hookbin.Infrastructure --startup-project src/Hookbin.API` |
| API exits at startup with `ValidateOptionsResult.Fail` | `AUTH_USERNAME` or `AUTH_PASSWORD_HASH` missing or hash lacks `$2` prefix | Set both vars in `.env`; use `dotnet run --project tools/RotatePassword -- --password '<pass>'` to generate the hash (or `python3 -c "import bcrypt; print(bcrypt.hashpw(b'pass', bcrypt.gensalt(12)).decode())"`) |
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
