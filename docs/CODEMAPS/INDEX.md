# Hookbin — Codemaps Index

**Last Updated:** 2026-05-14 (hard-delete on `DeleteTokenCommand` + EF cascade; StreamWorker drops FK-violation entries instead of poisoning PEL; new `DashboardMetricsApiTests` (×4) + `DashboardMetricsLifecycleE2ETests` (×2); deflaked `RedisSseBridgeServiceTests` startup wait)

---

## Overview

Hookbin is a self-hosted webhook debugging platform built with:
- **.NET 10** backend (Clean Architecture: Domain → Application → Infrastructure → API + StreamWorker + JobsWorker)
- **Angular 21** frontend SPA (standalone components, signals, custom modal via `@angular/cdk` Overlay — Material removed)
- **SQL Server 2022** with Testcontainers for integration testing
- **Redis 7** for token cache, request stream, SSE pub/sub, session revocation
- **SSE** for real-time request notifications

---

## Codemap Files

| File | Purpose | Audience |
|------|---------|----------|
| **architecture.md** | System layers, dependency graph, deployment units, key decisions | Architects, reviewers |
| **backend.md** | .NET project structure, controllers, CQRS map, key classes | Backend developers |
| **frontend.md** | Angular structure, services, components, routing | Frontend developers |
| **data.md** | Database schema, entity relationships, data flows (receive, SSE, custom response) | Full-stack developers |
| **dependencies.md** | NuGet/npm packages, versions, purpose, why used | Reviewers, upgraders |

---

## Quick Navigation

### For Backend Developers
1. Read **architecture.md** for the big picture
2. Read **backend.md** for module layout and CQRS handlers
3. Cross-reference **data.md** for entity relationships
4. See **dependencies.md** for .NET packages

### For Frontend Developers
1. Read **architecture.md** for the big picture
2. Read **frontend.md** for component structure and routing
3. Read **data.md** for API contracts and SSE behavior
4. See **dependencies.md** for npm packages

### For Full-Stack / DevOps
1. Read **architecture.md** for deployment units
2. Read **data.md** for critical data flows
3. See **dependencies.md** for all external services

---

## Recent Changes (as of 2026-05-14 — Hard-Delete + StreamWorker FK Handling)

### Hard-Delete on Token Removal (commit `5b693a3`)
- **`DeleteTokenCommandHandler`** — changed from `token.Deactivate()` (soft-delete) to `repository.DeleteAsync(token.Id)` (hard-delete). EF Core cascade (`WebhookRequestConfiguration.cs:40`, `OnDelete(DeleteBehavior.Cascade)`) removes all child `WebhookRequest` rows in the same transaction.
- **`IWebhookTokenRepository.DeleteAsync(Guid, CancellationToken)`** — new interface method; impl uses `FindAsync` + `Remove` + `SaveChangesAsync`.
- **Receiver semantic shift**: deleted tokens now return `404 Not Found` (was `410 Gone`). Deactivated tokens (`isActive=false` via `UpdateToken`) still return `410 Gone` and still persist requests for audit — this is the only path that exercises the audit-persistence invariant.
- **Dashboard contract restored**: stat tiles (`TOTAL ENDPOINTS`, `REQUESTS CAPTURED`, `LIVE ENDPOINTS`) now drop correctly after a delete. Pre-fix, the request-derived metrics counted `WebhookRequest` rows without joining on `IsActive`, leaving permanent ghost counts for any soft-deleted token.

### StreamWorker FK-Violation Handling (commit `e2bfee9`)
- **`RedisStreamConsumerService.ProcessEntryAsync`** — new catch on `DbUpdateException` where `InnerException is SqlException { Number: 547 }`. Treats FK violations as **terminal**: ACK + drop with a warning log.
- **Why**: hard-delete + async stream pipeline can race. A webhook POST publishes to Redis, then the token is deleted (cascade nukes existing rows), then the StreamWorker tries to INSERT a new `WebhookRequest` pointing at the now-gone token → SQL error 547. Without the catch, the entry stayed unACKed in the PEL forever, blocking every subsequent webhook for the worker's process lifetime.
- **Pre-fix CI symptom**: `StreamWorkerE2ETests.PostedWebhook_PersistsToDatabase_ViaStreamWorker` and 23 downstream E2E tests cascade-failed in CI run `25827159760`.

### Test Suite Updates
- **New: `tests/Hookbin.IntegrationTests/DashboardMetricsApiTests.cs`** — 4 tests covering the full delete-and-recount lifecycle through the metrics API.
- **New: `tests/Hookbin.E2ETests/DashboardMetricsLifecycleE2ETests.cs`** — 2 Playwright regression tests over the dashboard tile drop after delete.
- **Deflaked: `RedisSseBridgeServiceTests`** — replaced fixed `Task.Delay(50)` in startup helpers with `TaskCompletionSource` signalled from the captured `SubscribeAsync` callback (5 s timeout cap). Eliminates the CI flake on slow runners.
- **Updated counts**: integration 85 → 89; E2E 64 → 66; unit unchanged at 377; architecture unchanged at 47.

### DANGER ZONE Additions
- `DeleteTokenCommandHandler` must hard-delete (not soft-delete) — see CLAUDE.md DANGER ZONE row.
- StreamWorker must ACK + drop on `SqlException.Number == 547`. The retry-via-PEL pattern is correct for transient faults but **wrong** for FK violations that originate from hard-delete cascades.

---

## Recent Changes (as of 2026-05-11 — WebhookTokenRepository.UpdateAsync EF Core Fix)

### Bug Fix: EF Core Owned Entity Persistence
- **`WebhookTokenRepository.UpdateAsync`** — replaced `CurrentValues.SetValues(token)` with explicit mutation method calls
- **Root cause:** EF Core's `SetValues()` does not propagate to `OwnsOne`-mapped entities (like `CustomResponse`)
- **Failing tests:** `SetCustomResponse_Returns204_AndTokenReflectsChange`, `PostToWebhook_WithCustomResponse_ReturnsConfiguredStatus` (both now passing)
- **Fix:** Direct calls to `tracked.UpdateDescription()`, `tracked.Activate()`, `tracked.SetCustomResponse()`, etc. — matches original pre-refactor approach
- **Integration tests:** 59/59 passing
- **Full backend suite:** 442/443 passing (1 pre-existing flaky E2E retention test)

## Recent Changes (as of 2026-05-11 — Architecture Tests + Domain Encapsulation)

### Architecture Tests (commit 9062d68)
- **`tests/Hookbin.ArchitectureTests/`** — new project, 26 rules, ArchUnitNET 0.13.3 + NetArchTest.eNhancedEdition 1.4.5
- **Layer dependency tests (8)** — enforce Clean Architecture; fail on any `using Hookbin.Infrastructure` inside Domain etc.
- **CQRS convention tests (5)** — commands are `sealed record`, handlers are `internal sealed class`, validators inherit `AbstractValidator`
- **Repository/entity tests (4)** — interfaces in Domain, impls in Infrastructure, entities are sealed + immutable externally
- **Controller/middleware tests (3)** — public sealed controllers, consistent naming
- **Test project conventions (3)** — FA version uniformity (`FluentAssertions` 8.9.0 across all 3 test projects)
- **Folder-namespace tests (3)** — CLR namespace must match source file directory path
- **`architecture-test` CI job** — parallel (no `needs:`), completes <60s before integration/E2E starts
- **Scripts** — `scripts/run-arch-tests.ps1` (cross-OS PowerShell 7+) and `scripts/run-arch-tests.sh` (Bash)

### Domain Entity Encapsulation
- **`WebhookToken`** — `Description`, `IsActive`, `CustomResponse` now `private set;` + methods: `Activate`, `Deactivate`, `UpdateDescription`, `SetCustomResponse`, `ClearCustomResponse`
- **`WebhookRequest`** — `ProcessingTimeMs` now `private set;` + `RecordProcessingTime(long ms)`
- All 5 command handlers and test fixtures updated to use new mutation methods
- EF Core compatibility: reflection-based access, no `PropertyAccessMode` change needed

### WebhookOptionsValidator Namespace Fix
- Moved from `Hookbin.Application.Options` → `Hookbin.API.Options` (source file is in `src/Hookbin.API/Options/`)
- `Program.cs` uses alias; `WebhookOptionsValidatorTests.cs` updated with new `using`

### Test Count Update
- **Backend:** 443 tests (310 unit + 26 arch + 59 integration + 48 E2E) — all green
- **FluentAssertions** aligned to 8.9.0 across UnitTests + IntegrationTests + E2ETests

## Recent Changes (as of 2026-05-11 — Test Count Verification)

### Test Suite Accuracy Update (Verified 2026-05-11)
- **Backend unit:** 310 tests (verified via `dotnet test`)
- **Backend integration:** 59 tests (verified via `dotnet test`)
- **Backend E2E:** 48 tests (includes 4 test classes)
- **Frontend:** 118 tests in 9 spec files, 92.38% stmt / 84.83% branch / 90% fn / 93.51% line coverage
- **Total backend tests:** 417 (not 373 as previously noted)
- **All tests green** when infrastructure is available

### Post-2026-05-11 Fixes (No Architectural Changes)
Commits 3ccc331 → c060ac5 contain test reliability hardening:
- E2E race condition elimination (SseNotifier multi-subscriber, CustomResponse, Retention)
- Selector drift fixes (4 failing tests)
- Nginx readiness check hardening
- Auth hash environment variable handling in CI

No feature changes, no infrastructure changes, no breaking API changes.

## Recent Changes (as of 2026-05-11)

### Backend
- **PATCH `/api/tokens/{tokenId}/requests/{id}/note`** — new endpoint for per-request notes (max 2000 chars, null clears)
- **`SetRequestNoteCommand` / `SetRequestNoteCommandHandler` / `SetRequestNoteCommandValidator`** — full CQRS stack
- **`IWebhookRequestRepository.UpdateNoteAsync`** — `ExecuteUpdateAsync` (no EF tracking)
- **`WebhookRequestDetailDto`** — now includes `ProcessingTimeMs` (long?) and `Note` (string?) fields
- **`ProcessingTimeMs`** — computed in `RedisStreamConsumerService.ProcessEntryAsync` before `PersistAsync`

### Database
- **`WebhookRequests.ProcessingTimeMs`** — `BIGINT NULL` (migration `20260510104619_AddProcessingTimeMs`)
- **`WebhookRequests.Note`** — `NVARCHAR(2000) NULL` (migration `20260510104653_AddRequestNote`)
- **Covering index** `IX_WebhookRequests_TokenId_ReceivedAt_Id` (migration `20260507041721`)

### Frontend
- **`token-detail.component.ts`** — 3 new computed signals: `parsedQueryParams()`, `parsedFormValues()`, `threatLinks()`
- **Note UX** — inline edit (`noteEditing`, `noteValue`, `noteText`, `startNoteEdit`, `saveNote`, `cancelNoteEdit`)
- **`processingTimeMs` chip** — renders only when non-null; shows `"N ms"` in Pipeline detail row
- **Threat intelligence links** — Whois / Shodan / VirusTotal / Censys anchors with `target="_blank"` + `rel="noopener"`
- **`request.service.ts`** — `updateNote(tokenId, requestId, note)` → PATCH
- **Test suite** — Vitest, 9 spec files, 118 tests, all green; thresholds: 80/75/80/80 (stmt/branch/fn/line)

### CI
- **`.github/workflows/ci.yml`** — `--settings tests/coverlet.runsettings` added to unit + integration dotnet test
- **Frontend `coverage` step** — `npm test -- --watch=false --coverage` added to `frontend` job with artifact upload
- **New `e2e-test` job** — uses `rebuild-and-wait.sh`, installs Chromium, runs E2E suite, uploads traces on failure

### Tests (total)
- **Backend unit:** 310 tests (all green)
- **Frontend:** 118 tests (all green), 92%/84%/90%/93% stmt/branch/fn/line coverage
- **E2E new:** 7 new tests in `NewFeatureE2ETests` (form body, processing time, notes, SSE live, threat links, export, delete+clear)

## Recent Changes (as of 2026-05-10)

### Architecture
- **Three-process split:** `stream-worker` (Redis stream consumer) and `jobs-worker` (retention cleanup) extracted from API into separate deployable units
- **Redis added:** `redis:7-alpine` for stream (`webhook-requests`), pub/sub (`sse:{tokenId}`), and token cache backing
- **DI extensions split:** `AddCoreInfrastructure` + `AddApiInfrastructure` + `AddStreamWorkerInfrastructure` + `AddJobsWorkerInfrastructure`

### New Interfaces
- **`IRequestQueuePublisher`** (Domain) → `RedisStreamPublisher` — decouples `WebhookController` from Redis XADD
- **`ITokenCache`** (Application/Caching) → `RedisTokenCache` — wraps `IMemoryCache` with typed contract
- **`ISessionRevocationStore`** (API/Services) → `RedisSessionRevocationStore` — session revocation on logout

### Infrastructure
- **`RedisStreamConsumerService`** — decoupled from `ISseNotifier`; now publishes only via Redis pub/sub
- **`RedisSseBridgeService`** — stays in API; subscribes to `sse:*` and writes to in-process `SseNotifier`
- **Consumer name** — stable via `HOOKBIN_WORKER_ID` env var (prevents PEL orphaning on restart)
- **Dockerfile** — parameterized with `ARG PROJECT_NAME`; `curl` installed for health checks

### Tests (as of 2026-05-10)
- **373 tests total:** 286 unit + 47 integration + 40 E2E (all green)
- **7 new branch-coverage tests** for `RedisStreamConsumerService` error containment zones
- **New E2E tests:** `StreamWorkerE2ETests`, `JobsWorkerRetentionTests`, `ComprehensiveE2ETests`

## Recent Changes (as of 2026-05-08)

### Backend Updates
- **GetTokenQueryHandler.cs** — switched from `IConfiguration` to `IOptions<WebhookOptions>` (consistent with `CreateTokenCommandHandler`)
- **GetTokensQueryHandler.cs** — same change; all three token query/command handlers now use the validated Options pattern
- **appsettings.json** — `Webhook:BaseUrl` default changed from `http://localhost:5000` → `""` (startup validator now fires when `HOOKBIN_BASE_URL` not set)
- **appsettings.Development.json** — `Webhook:BaseUrl` changed from `http://localhost:5000` → `http://localhost:8080` (matches Angular dev proxy port)

### Infrastructure Updates
- **docker-compose.override.yml** — `Hookbin__BaseUrl` was hardcoded as `http://localhost:8088`, silently overriding `.env`. Fixed to `${HOOKBIN_BASE_URL}` so both local and ngrok modes work correctly.
- **.env.example** — updated with two documented examples (local docker-compose and ngrok mode) plus note that URL is computed at read time

### Test Updates
- **DashboardE2ETests.cs** — added `TokenDetail_WebhookUrl_UsesConfiguredBaseUrl` regression test (verifies webhook URL uses `HOOKBIN_BASE_URL`, not localhost)
- **DashboardE2EFixture** — added XSRF-TOKEN priming GET after login (fixes antiforgery header for write API calls in E2E)
- **TokenDetail_DeleteToken_RedirectsToDashboard** — fixed to use Angular Material `ConfirmDialogComponent` selector instead of `page.Dialog` (browser dialog never fires)

## Recent Changes (as of 2026-05-07)

### Backend Updates
- **WebhookController.cs** — custom response headers now deserialized and applied to responses
- **Program.cs** — rate limiting (`webhook-receiver` policy), antiforgery (`X-XSRF-TOKEN`), session revocation store, ForwardedHeaders
- **SseNotifier.cs** — per-token subscriber cap (max 10, returns 429 on 11th)
- **WebhookRequestRepository.cs** — batched retention cleanup (5k rows/loop), search constrained to Method/Path/IpAddress/StatusCode

### Frontend Updates
- **app.ts** + **app.html** — logout button wired to `AuthService`; conditional on `auth.isAuthenticated()`
- **token.service.ts** — `setCustomResponse` return type corrected to `Observable<void>` (backend returns 204)
- **token-detail.component.ts** — custom-response save rebuilds signal from DTO instead of calling `token.set(null)`
- **token-detail.component.html** — search empty state: "No requests yet" vs "No matching requests found"

### Infrastructure Updates
- **nginx.conf** — CSP updated to allow `fonts.gstatic.com` and `fonts.googleapis.com`
- **HashGen/** (renamed from `RotatePassword`) — BCrypt hash generator CLI
- **New tests** — `SessionRevocationStoreTests.cs`, `SseNotifierTests.cs`, `NoOpAntiforgery.cs`

---

## Key Entry Points

| Layer | File | Purpose |
|-------|------|---------|
| **API** | `src/Hookbin.API/Program.cs` | DI (Core+Api), middleware, endpoint mapping |
| **StreamWorker** | `src/Hookbin.StreamWorker/Program.cs` | DI (Core+Stream), Polly DB wait, health endpoints |
| **JobsWorker** | `src/Hookbin.JobsWorker/Program.cs` | DI (Core+Jobs), Polly DB wait, health endpoints |
| **Controllers** | `src/Hookbin.API/Controllers/WebhookController.cs` | `POST /webhook/{token:guid}` receiver |
| **Middleware** | `src/Hookbin.API/Middleware/GlobalExceptionMiddleware.cs` | Exception → HTTP status, SSE guards |
| **Domain** | `src/Hookbin.Domain/Entities/WebhookToken.cs` | Token aggregate root |
| **Application** | `src/Hookbin.Application/Tokens/Commands/` | CQRS command handlers |
| **Infrastructure** | `src/Hookbin.Infrastructure/DependencyInjection.cs` | Four DI extensions; Redis + EF registrations |
| **Infrastructure** | `src/Hookbin.Infrastructure/Sse/SseNotifier.cs` | Channel-based SSE broadcast |
| **Frontend** | `frontend/hookbin-spa/src/app/app.ts` | Root Angular component, auth, logout |
| **Services** | `frontend/hookbin-spa/src/app/core/services/sse.service.ts` | EventSource SSE client |

---

## References

- **Full README:** `README.md` (single source of truth for design, security, troubleshooting)
- **Agent guide:** `CLAUDE.md` (commands, key non-obvious facts)
- **Architecture:** README.md §5 HLD, §7 LLD
- **Data flows:** README.md §6
- **CQRS map:** README.md §7.5
- **Security model:** README.md §11
- **Testing guide:** README.md §13
- **Troubleshooting:** README.md §20
