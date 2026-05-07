<!-- Generated: 2026-05-07 | Updated: rate limiting, antiforgery, session revocation, per-token SSE cap (max 10), custom response headers deserialization -->

# Architecture

## Project Type
Full-stack monorepo — .NET 10 Clean Architecture API + Angular 21 SPA + SQL Server + SEQ + optional ngrok tunnel

## System Diagram
```
Browser (Angular 21)
  │  HTTP (cookies)         EventSource (SSE, withCredentials)
  ▼                                     ▼
Nginx :8088
  │  /api/* /webhook/* /health → api:8080
  │  / → static Angular bundle
  ▼
ASP.NET Core API :8080
  │  ForwardedHeaders (ForwardLimit=2, X-Forwarded-For / X-Forwarded-Proto)
  │  GlobalExceptionMiddleware → RateLimiter → Authentication → Authorization
  ▼
Application Layer (MediatR CQRS)
  │  ValidationBehavior (FluentValidation) → LoggingBehavior → Handler
  ▼
Infrastructure Layer
  ├── EF Core → SQL Server (WebhookTokens, WebhookRequests)
  ├── SseNotifier (ConcurrentDictionary<Guid, Channel<SseEvent>>)
  └── RetentionCleanupService (PeriodicTimer 24h)

Optional ngrok tunnel (docker-compose.ngrok.yml):
  Browser ──HTTPS──> ngrok:4040 ──> Nginx:80
```

## Dependency Rule (Clean Architecture)
```
API → Application → Domain
Infrastructure → Application → Domain
(Domain has zero references to outer layers)
```

## Auth Flow
```
POST /api/auth/login (AllowAnonymous, rate-limited 5/min)
  → BCrypt verify → SignInAsync (cookie)
GET  /api/auth/me   → returns username
POST /api/auth/logout → SignOutAsync
```

## Webhook Receive Flow
```
POST /webhook/{guid} (AllowAnonymous)
  → cache.Get("token:{guid}") or GetByTokenIncludingInactiveAsync (includes inactive)
  → wrap body-read in try/catch (IOException → BadHttpRequestException)
  → persist WebhookRequest (always, even for inactive tokens — audit trail)
  ├─ Active token: sseNotifier.NotifyAsync(tokenId, summary) + return CustomResponse or 200
  └─ Inactive token: return 410 Gone (signals sender to stop retrying)
```

## SSE Flow
```
GET /api/tokens/{id}/sse (authenticated)
  → SseNotifier.SubscribeAsync creates Channel
  → stream events until client disconnect or cancel
  → wire event name: "event: request"
  → max 10 concurrent connections per token
```

## Reverse Proxy (Nginx)

Maps `X-Forwarded-Proto` header via `docker/frontend/nginx-maps.conf` (00-maps.conf):
- Preserves upstream proto (e.g. ngrok https) or falls back to direct scheme
- SSE routes use `proxy_buffering off` and 3600s timeout
- All backends use `$forwarded_scheme` instead of `$scheme` for cookie `Secure` flag

## Resilience & Database

SQL Server connection pool:
- Retry on transient failures: `EnableRetryOnFailure(3, 2s)` in DependencyInjection.cs
- Automatic exponential backoff: 1s → 2s between retries
- Request ReceivedAt timestamp: `datetimeoffset(7)` for millisecond precision (migration 20260506202000_PinReceivedAtPrecision)
- Pagination ordering: `ReceivedAt DESC, THEN Id DESC` for deterministic results

## Rate Limiting & Security (Updated 2026-05-07)

- **Webhook receiver:** `webhook-receiver` fixed-window rate limiter (5/sec default, configurable via WebhookOptions.ReceiverRateLimitPerSecond)
- **Login brute-force:** 5 attempts per 1 minute (fixed-window, 5/min)
- **Antiforgery:** `X-XSRF-TOKEN` header required on state-changing requests; cookies are HttpOnly + Strict SameSite
- **Session revocation:** In-memory store (`SessionRevocationStore`) — logout revokes all active sessions instantly
- **SSE subscriber cap:** Max 10 concurrent connections per token; 11th request returns 429 Too Many Requests
- **ForwardedHeaders:** `X-Forwarded-For`, `X-Forwarded-Proto` trusted from Nginx (ForwardLimit=2)
- **Custom response headers:** Deserialized from JSON string; headers applied directly to `Response.Headers` in receiver

## Rate Limiting & Security (as of 2026-05-07)

- **Webhook receiver:** `webhook-receiver` fixed-window policy (permit limit + window duration from WebhookOptions)
- **Login:** 5 attempts per 1 minute (brute-force protection)
- **Antiforgery:** `X-XSRF-TOKEN` header required on state-changing requests
- **Session revocation:** In-memory store (`SessionRevocationStore`) — logout revokes all active sessions
- **SSE subscriber cap:** Max 10 concurrent connections per token; 11th request returns 429 Too Many Requests
- **ForwardedHeaders:** `X-Forwarded-For`, `X-Forwarded-Proto` trusted from Nginx (ForwardLimit=2)
- **Custom response headers:** Deserialized from JSON string at receiver; headers applied to webhook response
