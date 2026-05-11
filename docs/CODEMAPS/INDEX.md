# Webhook Service — Codemaps Index

**Last Updated:** 2026-05-11 (per-request notes PATCH endpoint; processingTimeMs + note fields; frontend computed signals; Vitest coverage thresholds; CI coverage enforcement + E2E job)

---

## Overview

Webhook Inspector is a self-hosted webhook debugging platform built with:
- **.NET 10** backend (Clean Architecture: Domain → Application → Infrastructure → API)
- **Angular 21** frontend SPA (standalone components, Angular Material)
- **SQL Server 2022** with Testcontainers for integration testing
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
- **Consumer name** — stable via `WEBHOOK_WORKER_ID` env var (prevents PEL orphaning on restart)
- **Dockerfile** — parameterized with `ARG PROJECT_NAME`; `curl` installed for health checks

### Tests (as of 2026-05-10)
- **373 tests total:** 286 unit + 47 integration + 40 E2E (all green)
- **7 new branch-coverage tests** for `RedisStreamConsumerService` error containment zones
- **New E2E tests:** `StreamWorkerE2ETests`, `JobsWorkerRetentionTests`, `ComprehensiveE2ETests`

## Recent Changes (as of 2026-05-08)

### Backend Updates
- **GetTokenQueryHandler.cs** — switched from `IConfiguration` to `IOptions<WebhookOptions>` (consistent with `CreateTokenCommandHandler`)
- **GetTokensQueryHandler.cs** — same change; all three token query/command handlers now use the validated Options pattern
- **appsettings.json** — `Webhook:BaseUrl` default changed from `http://localhost:5000` → `""` (startup validator now fires when `WEBHOOK_BASE_URL` not set)
- **appsettings.Development.json** — `Webhook:BaseUrl` changed from `http://localhost:5000` → `http://localhost:8080` (matches Angular dev proxy port)

### Infrastructure Updates
- **docker-compose.override.yml** — `Webhook__BaseUrl` was hardcoded as `http://localhost:8088`, silently overriding `.env`. Fixed to `${WEBHOOK_BASE_URL}` so both local and ngrok modes work correctly.
- **.env.example** — updated with two documented examples (local docker-compose and ngrok mode) plus note that URL is computed at read time

### Test Updates
- **DashboardE2ETests.cs** — added `TokenDetail_WebhookUrl_UsesConfiguredBaseUrl` regression test (verifies webhook URL uses `WEBHOOK_BASE_URL`, not localhost)
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
| **API** | `src/WebhookService.API/Program.cs` | DI (Core+Api), middleware, endpoint mapping |
| **StreamWorker** | `src/WebhookService.StreamWorker/Program.cs` | DI (Core+Stream), Polly DB wait, health endpoints |
| **JobsWorker** | `src/WebhookService.JobsWorker/Program.cs` | DI (Core+Jobs), Polly DB wait, health endpoints |
| **Controllers** | `src/WebhookService.API/Controllers/WebhookController.cs` | `POST /webhook/{token:guid}` receiver |
| **Middleware** | `src/WebhookService.API/Middleware/GlobalExceptionMiddleware.cs` | Exception → HTTP status, SSE guards |
| **Domain** | `src/WebhookService.Domain/Entities/WebhookToken.cs` | Token aggregate root |
| **Application** | `src/WebhookService.Application/Tokens/Commands/` | CQRS command handlers |
| **Infrastructure** | `src/WebhookService.Infrastructure/DependencyInjection.cs` | Four DI extensions; Redis + EF registrations |
| **Infrastructure** | `src/WebhookService.Infrastructure/Sse/SseNotifier.cs` | Channel-based SSE broadcast |
| **Frontend** | `frontend/webhook-spa/src/app/app.ts` | Root Angular component, auth, logout |
| **Services** | `frontend/webhook-spa/src/app/core/services/sse.service.ts` | EventSource SSE client |

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
