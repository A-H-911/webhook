<!-- Generated: 2026-05-06 | Files scanned: 105 | Token estimate: ~650 -->

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
  → cache.Get("token:{guid}") or DB lookup
  → persist WebhookRequest
  → sseNotifier.NotifyAsync(tokenId, summary)
  → return CustomResponse or 200
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
