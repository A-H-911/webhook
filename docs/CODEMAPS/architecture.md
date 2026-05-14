<!-- Generated: 2026-05-14 | Files scanned: 170 (59 backend src + 50 tests + 61 frontend) | Token estimate: ~960 -->

# Architecture

## Project Type
Full-stack monorepo — .NET 10 Clean Architecture + Angular 21 SPA + SQL Server 2022 + Redis 7 + SEQ.

**Three deployable units** share Domain/Application/Infrastructure libraries, SQL DB, and Redis:

| Unit | Role |
|------|------|
| `Hookbin.API` | HTTP endpoints, SSE fan-out, cookie auth, antiforgery, rate limiting |
| `Hookbin.StreamWorker` | XREADGROUP `webhook-requests` → persist → PUBLISH `sse:{tokenId}` |
| `Hookbin.JobsWorker` | Retention cleanup (24h PeriodicTimer) — single replica only |

## System Diagram
```
Browser (Angular 21, standalone components)
  │  HTTP (cookies, X-XSRF-TOKEN)        EventSource (SSE, withCredentials)
  ▼                                                  ▼
Nginx :8088
  │  /api/* /webhook/* /health → api:8080
  │  / → static Angular bundle
  ▼
Hookbin.API :8080
  │  ForwardedHeaders → GlobalExceptionMiddleware → RateLimiter → Auth(cookie) → Antiforgery
  │
  ├── POST /webhook/{guid}        → IRequestQueuePublisher → XADD webhook-requests
  ├── GET  /api/tokens/{id}/sse   → SseNotifier Channel<SseEvent>
  │        ↑ written by RedisSseBridgeService (SUBSCRIBE sse:*)
  └── CRUD /api/tokens, /api/tokens/{id}/requests (MediatR CQRS, FluentValidation)
                                                     │
                                                     ▼
Redis
  ├── Stream  webhook-requests (XREADGROUP → StreamWorker → XACK)
  ├── Pub/sub sse:{tokenId}     (PUBLISH → API RedisSseBridgeService → SseNotifier)
  ├── Cache   token:{guid}      (5 min sliding, JSON via System.Text.Json)
  └── Set     session:revoked   (logout = instant cluster-wide invalidation)

Hookbin.StreamWorker
  └── RedisStreamConsumerService: XREADGROUP "0-0" (PEL recovery) → ">" (new) → persist → XACK → PUBLISH sse:{tokenId}
       Consumer name: HOOKBIN_WORKER_ID (default "consumer-{MachineName}")

Hookbin.JobsWorker
  └── RetentionCleanupService: 24h PeriodicTimer → DELETE old WebhookRequests (5k batched)

SQL Server: WebhookTokens, WebhookRequests
SEQ:        structured log sink (all three units, OTel-friendly schema)
```

## Dependency Rule (Clean Architecture)
```
API           → Application → Domain
Infrastructure → Application → Domain
StreamWorker  → Infrastructure → Application → Domain
JobsWorker    → Infrastructure → Application → Domain
(Domain has zero references to outer layers — enforced by tests)
```

**Enforced by `tests/Hookbin.ArchitectureTests/` (47 tests, ArchUnitNET 0.13.3 + NetArchTest.eNhancedEdition 1.4.5):**
- `Layers/LayerDependencyTests.cs` (8) — dependency direction
- `Conventions/CqrsConventionTests.cs` (5) — sealed records, internal sealed handlers
- `Conventions/RepositoryEntityConventionTests.cs` (4) — repository placement, entity immutability
- `Conventions/ControllerMiddlewareConventionTests.cs` (3) — controller suffix, middleware namespace
- `Conventions/TestProjectConventionTests.cs` (3) — FluentAssertions version uniformity
- `Conventions/ZeroTrustInvariantsTests.cs` (12) — DANGER ZONE structural guards (private-set `[JsonInclude]`, `AsNoTracking`, `[AllowAnonymous]`, worker `MigrateAsync`)
- `Conventions/OperationalSnapshotTests.cs` (4) — docker-compose replicas, nginx SSE buffering, worker `.csproj` design-tools exclusion
- `Structure/FolderNamespaceTests.cs` (3) — folder ↔ namespace alignment
- `Domain/EntityEncapsulationTests.cs` (5) — no public setters on entities

## DI Extension Map
| Extension | Registers | Called by |
|-----------|-----------|-----------|
| `AddCoreInfrastructure(IConfiguration)` | `ApplicationDbContext`, `IWebhookRequestRepository`, `IWebhookTokenRepository`, `IConnectionMultiplexer` | API + StreamWorker + JobsWorker |
| `AddApiInfrastructure()` | `IRequestQueuePublisher→RedisStreamPublisher`, `ITokenCache→RedisTokenCache`, `ISseNotifier→SseNotifier`, `ISessionRevocationStore→RedisSessionRevocationStore`, `RedisSseBridgeService` (HostedService) | API only |
| `AddStreamWorkerInfrastructure()` | `RedisStreamConsumerService` (HostedService) | StreamWorker only |
| `AddJobsWorkerInfrastructure()` | `RetentionCleanupService` (HostedService) | JobsWorker only |

## Auth Flow
```
POST /api/auth/login (AllowAnonymous, rate-limited 5/min)
  → BCrypt verify → SignInAsync (cookie) → ISessionRevocationStore.IssueAsync
GET  /api/auth/me   → username + version
POST /api/auth/logout → ISessionRevocationStore.RevokeAsync → SignOutAsync
```

## Webhook Receive Flow
```
POST /webhook/{guid} (AllowAnonymous, rate-limited, [JsonInclude]-safe cache)
  → ITokenCache.GetOrLoadAsync("token:{guid}") — 5min sliding
       └── miss path uses GetByTokenIncludingInactiveAsync (active + inactive)
  → read body (IOException → BadHttpRequestException → 400)
  → IRequestQueuePublisher.PublishAsync → XADD webhook-requests
  ├── Active   → return CustomResponse or 200
  ├── Inactive (isActive=false)        → return 410 Gone (request still persisted for audit)
  └── Deleted  (row hard-deleted)      → return 404 Not Found (no row, no persistence)

StreamWorker: XREADGROUP → IWebhookRequestRepository.AddAsync → XACK → PUBLISH sse:{tokenId}
  └── On SqlException.Number==547 (FK violation, hard-delete race) → ACK and drop
API: SUBSCRIBE sse:* → SseNotifier.NotifyAsync → Channel<SseEvent> → "event: request"
```

## Token Lifecycle (hard-delete vs deactivate)
| Path | Trigger | Token row | WebhookRequests | Receiver response |
|---|---|---|---|---|
| Hard-delete | `DELETE /api/tokens/{id}` → `DeleteTokenCommand` | removed | cascaded via EF Core `OnDelete(Cascade)` | `404 Not Found` |
| Deactivate | `PUT /api/tokens/{id}` with `isActive=false` → `UpdateTokenCommand` | kept (`IsActive=false`) | preserved + new requests still persisted | `410 Gone` |
| Reactivate | `PUT /api/tokens/{id}` with `isActive=true` | row remains, `IsActive=true` | retained | normal `200`/`CustomResponse` |

## Rate Limiting & Security
| Boundary | Policy | Source |
|---|---|---|
| `/api/auth/login` | Fixed window, 5/min | `AddRateLimiter` `login` policy |
| `POST /webhook/{guid}` | Token bucket, per-route | `webhook-receiver` policy, `WebhookOptions.ReceiverRateLimitPerSecond` |
| Cluster-wide session revocation | Redis set `session:revoked` | `ISessionRevocationStore` |
| CSRF | `X-XSRF-TOKEN` header on writes | `AddAntiforgery` |
| SSE concurrency | Max 10/token (11th → 429) | `SseNotifier` semaphore |

## Reverse Proxy (Nginx)
- SSE: `proxy_buffering off`, `proxy_read_timeout 3600s`
- Header maps via `docker/frontend/nginx-maps.conf` — `$forwarded_scheme` drives cookie `Secure`

## Resilience
- EF Core: `EnableRetryOnFailure(3, 2s)`
- Worker DB readiness: Polly retry 30× exponential up to 30s via `CanConnectAsync` (never `MigrateAsync` — API only)
- Stable consumer name across restarts: `HOOKBIN_WORKER_ID`
- PEL recovery on cold start: `XREADGROUP "0-0"` drains unACKed messages

## Test Coverage (2026-05-14)
| Layer | Tests | Mutation Score |
|---|---:|---:|
| Unit | 377 | Domain 86.4% / Application 90.4% (Stryker.NET 4.14.1) |
| Architecture | 47 | n/a (structural) |
| Integration | 89 | n/a (covers Infrastructure paths) — +4 `DashboardMetricsApiTests` |
| E2E (Playwright) | 66 | n/a — +2 `DashboardMetricsLifecycleE2ETests` |
| Frontend (Vitest) | 209 | n/a |
| **Total** | **788** | API 73.4% / Infrastructure 52.7% (integration-tested) |

Audit artifacts: `docs/AUDIT/BASELINE.md`, `docs/AUDIT/REMEDIATION.md`.

## Deployment Notes
- `HOOKBIN_BASE_URL` precedence: override → compose → appsettings.Development → appsettings (validator fires if empty)
- `.env` BCrypt hash: must be single-quoted (`'$2b$12$...'`) — `$letter` triggers compose variable expansion
- `jobs-worker` runs as `deploy.replicas: 1` (no leader election)
- All env vars: `docs/ENV.md`
