<!-- Generated: 2026-05-05 | Files scanned: 95 | Token estimate: ~750 -->

# Backend Architecture

## Routes → Handler Map

### Auth (AllowAnonymous)
```
POST /api/auth/login        → AuthController.Login
GET  /api/auth/me           → AuthController.Me
POST /api/auth/logout       → AuthController.Logout
```

### Tokens (requires auth)
```
GET    /api/tokens                        → GetTokensQuery → GetTokensQueryHandler
GET    /api/tokens/{id}                   → GetTokenQuery  → GetTokenQueryHandler
POST   /api/tokens                        → CreateTokenCommand → CreateTokenCommandHandler
PUT    /api/tokens/{id}                   → UpdateTokenCommand → UpdateTokenCommandHandler
DELETE /api/tokens/{id}                   → DeleteTokenCommand → DeleteTokenCommandHandler
PUT    /api/tokens/{id}/custom-response   → SetCustomResponseCommand → SetCustomResponseCommandHandler
DELETE /api/tokens/{id}/custom-response   → ResetCustomResponseCommand → ResetCustomResponseCommandHandler
GET    /api/tokens/{id}/sse               → SseController.Subscribe
```

### Requests (requires auth, scoped to token)
```
GET    /api/tokens/{tokenId}/requests             → GetRequestsQuery (paginated, searchable)
GET    /api/tokens/{tokenId}/requests/{id}        → GetRequestByIdQuery
GET    /api/tokens/{tokenId}/requests/{id}/export → ExportRequestQuery → File(json)
DELETE /api/tokens/{tokenId}/requests             → ClearRequestsCommand
DELETE /api/tokens/{tokenId}/requests/{id}        → DeleteRequestCommand
```

### Webhook Receiver (AllowAnonymous)
```
*/webhook/{token:guid}   → WebhookController.Receive (GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS)
```

### Health
```
GET /health/live   → AllowAnonymous, no checks
GET /health/ready  → AllowAnonymous, SqlServer ping
```

## Middleware Chain (ordered)
```
ForwardedHeaders → GlobalExceptionMiddleware → CORS → RateLimiter
→ Authentication → Authorization → Controllers
```

## Key Files
```
src/WebhookService.API/Program.cs                               (DI, middleware pipeline, 166 lines)
src/WebhookService.API/Middleware/GlobalExceptionMiddleware.cs  (exception→HTTP, SSE guard)
src/WebhookService.API/Controllers/AuthController.cs           (login/logout/me)
src/WebhookService.API/Controllers/WebhookController.cs        (receiver, cache, SSE notify)
src/WebhookService.API/Controllers/SseController.cs            (SSE subscribe/stream)
src/WebhookService.API/Controllers/TokensController.cs         (CRUD + custom-response)
src/WebhookService.API/Controllers/RequestsController.cs       (paging, export, delete)
src/WebhookService.Application/Common/Behaviors/ValidationBehavior.cs
src/WebhookService.Application/Common/Behaviors/LoggingBehavior.cs
src/WebhookService.Infrastructure/Sse/SseNotifier.cs           (ConcurrentDictionary<Guid, Channel>)
src/WebhookService.Infrastructure/BackgroundServices/RetentionCleanupService.cs
```

## Options (validated at startup)
```
Webhook:BaseUrl          (required) — used to build webhook URLs
Webhook:RetentionDays    — requests older than N days cleaned up
Webhook:MaxRequestSizeMb — Kestrel body size limit
Auth:Username            (required)
Auth:PasswordHash        (required, must start with $2 — BCrypt)
Auth:SessionHours        — cookie sliding expiry
```

## IDOR Protection
`GetRequestById`, `ExportRequest`, `DeleteRequest` all include `WHERE TokenId = @tokenId`
