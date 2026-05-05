<!-- Generated: 2026-05-05 | Files scanned: 95 | Token estimate: ~600 -->

# Architecture

## Project Type
Full-stack monorepo — .NET 10 Clean Architecture API + Angular 21 SPA + SQL Server + SEQ

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
  │  GlobalExceptionMiddleware → RateLimiter → Authentication → Authorization
  │  ForwardedHeaders (X-Forwarded-For / X-Forwarded-Proto)
  ▼
Application Layer (MediatR CQRS)
  │  ValidationBehavior (FluentValidation) → LoggingBehavior → Handler
  ▼
Infrastructure Layer
  ├── EF Core → SQL Server (WebhookTokens, WebhookRequests)
  ├── SseNotifier (ConcurrentDictionary<Guid, Channel<SseEvent>>)
  └── RetentionCleanupService (PeriodicTimer 24h)
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
