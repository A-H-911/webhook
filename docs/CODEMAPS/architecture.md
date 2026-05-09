<!-- Generated: 2026-05-10 | Updated: three-process architecture (api + stream-worker + jobs-worker); Redis pub/sub SSE fan-out; DI split; IRequestQueuePublisher/ITokenCache/ISessionRevocationStore interfaces -->

# Architecture

## Project Type
Full-stack monorepo — .NET 10 Clean Architecture + Angular 21 SPA + SQL Server + Redis + SEQ + optional ngrok tunnel

**Three deployable units** share the same Domain/Application/Infrastructure libraries, SQL DB, and Redis:

| Unit | Role |
|------|------|
| `WebhookService.API` | HTTP endpoints, SSE fan-out (cookie auth, rate limiting, antiforgery) |
| `WebhookService.StreamWorker` | Drains `webhook-requests` Redis stream → persists to SQL → publishes `sse:{tokenId}` |
| `WebhookService.JobsWorker` | Retention cleanup (24h PeriodicTimer); single replica only |

## System Diagram
```
Browser (Angular 21)
  │  HTTP (cookies, XSRF)        EventSource (SSE, withCredentials)
  ▼                                           ▼
Nginx :8088
  │  /api/* /webhook/* /health → api:8080
  │  / → static Angular bundle
  ▼
WebhookService.API :8080
  │  ForwardedHeaders → GlobalExceptionMiddleware → RateLimiter → Auth → Antiforgery
  │
  ├── POST /webhook/{guid} → IRequestQueuePublisher → XADD webhook-requests stream
  │
  ├── GET  /api/tokens/{id}/sse → SseNotifier (in-process Channel<SseEvent>)
  │        ↑ written by RedisSseBridgeService (SUBSCRIBE sse:* pub/sub channel)
  │
  └── CRUD /api/tokens, /api/tokens/{id}/requests (MediatR CQRS)

Redis
  ├── Stream:  webhook-requests (XREADGROUP → StreamWorker)
  └── Pub/sub: sse:{tokenId}   (PUBLISH → API's RedisSseBridgeService → SseNotifier)

WebhookService.StreamWorker
  ├── RedisStreamConsumerService: XREADGROUP → persist to SQL → PUBLISH sse:{tokenId}
  └── Consumer name: WEBHOOK_WORKER_ID env var, fallback: "consumer-{MachineName}"

WebhookService.JobsWorker
  └── RetentionCleanupService: PeriodicTimer (24h) → DELETE old WebhookRequests (batched 5k/loop)

SQL Server: WebhookTokens, WebhookRequests (shared by all three units)
SEQ: structured log sink (shared by all three units)
```

## Dependency Rule (Clean Architecture)
```
API → Application → Domain
Infrastructure → Application → Domain
StreamWorker → Infrastructure → Application → Domain
JobsWorker   → Infrastructure → Application → Domain
(Domain has zero references to outer layers)
```

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
GET  /api/auth/me   → returns username
POST /api/auth/logout → ISessionRevocationStore.RevokeAsync → SignOutAsync
```

## Webhook Receive Flow
```
POST /webhook/{guid} (AllowAnonymous, rate-limited)
  → ITokenCache.GetOrLoadAsync("token:{guid}") or DB lookup (includes inactive)
  → read body (try/catch IOException → BadHttpRequestException)
  → IRequestQueuePublisher.PublishAsync → XADD webhook-requests stream
  ├── Active token:   return CustomResponse (or 200)
  └── Inactive token: return 410 Gone
  (request persisted by StreamWorker; audit trail always maintained)

StreamWorker picks up from stream:
  → XREADGROUP webhook-requests → deserialize → IWebhookRequestRepository.AddAsync → XACK
  → ISubscriber.PublishAsync("sse:{tokenId}", summaryJson)

API RedisSseBridgeService:
  → SUBSCRIBE sse:* → SseNotifier.NotifyAsync → Channel<SseEvent> → SSE HTTP response
```

## SSE Flow
```
GET /api/tokens/{id}/sse (authenticated)
  → SseNotifier.SubscribeAsync creates Channel<SseEvent>
  → stream events until client disconnect or cancel
  → wire event name: "event: request"
  → max 10 concurrent connections per token (11th returns 429)

Fan-out path: PUBLISH sse:{tokenId} → RedisSseBridgeService → SseNotifier.NotifyAsync
```

## Reverse Proxy (Nginx)
- SSE routes use `proxy_buffering off` and 3600s timeout
- `X-Forwarded-Proto` via `docker/frontend/nginx-maps.conf` (00-maps.conf)
- All backends use `$forwarded_scheme` for cookie `Secure` flag

## Resilience
- EF Core retry: `EnableRetryOnFailure(3, 2s)`
- Worker DB readiness: Polly retry (30×, exponential up to 30s) via `CanConnectAsync`
- StreamWorker consumer name stable across restarts via `WEBHOOK_WORKER_ID` env var
- PEL recovery: `XREADGROUP "0-0"` drains unACKed messages from previous consumer run

## Rate Limiting & Security
- **Webhook receiver:** `webhook-receiver` fixed-window (configurable via `WebhookOptions.ReceiverRateLimitPerSecond`)
- **Login brute-force:** 5 attempts per 1 minute
- **Antiforgery:** `X-XSRF-TOKEN` required on state-changing requests
- **Session revocation:** `ISessionRevocationStore` — logout revokes all active sessions instantly
- **SSE subscriber cap:** Max 10 concurrent connections per token

## Deployment Notes
**WEBHOOK_BASE_URL precedence** (highest → lowest):
1. `docker-compose.override.yml` → `Webhook__BaseUrl: "${WEBHOOK_BASE_URL}"`
2. `docker-compose.yml` → `Webhook__BaseUrl: "${WEBHOOK_BASE_URL}"`
3. `appsettings.Development.json` → `http://localhost:8080`
4. `appsettings.json` → `""` (empty — startup validator fires if unset)

**`.env` BCrypt hash quoting:** Values containing `$letter...` are treated as variable references by Docker Compose. Single-quote the hash: `AUTH_PASSWORD_HASH='$2b$12$...'`.

**`jobs-worker` must run as single replica** — `RetentionCleanupService` has no leader election. Use `deploy.replicas: 1` in compose.
