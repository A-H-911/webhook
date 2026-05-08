<!-- Generated: 2026-05-08 | Updated: GetToken/GetTokens handlers now use IOptions<WebhookOptions> instead of IConfiguration -->

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

### Webhook Receiver (AllowAnonymous, rate-limited)
```
*/webhook/{token:guid}   → WebhookController.Receive (GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS)
  [EnableRateLimiting("webhook-receiver")]
  ├─ Active token:      custom status + custom headers + custom body (if set) + SSE notify + 200
  ├─ Inactive token:    410 Gone + persists request for audit (no SSE)
  └─ Rate limited:      429 Too Many Requests
```

### Health
```
GET /health/live   → AllowAnonymous, no checks
GET /health/ready  → AllowAnonymous, SqlServer ping
```

## Middleware Chain (ordered)
```
ForwardedHeaders → GlobalExceptionMiddleware → CORS → RateLimiter → Authentication → Authorization
  ├─ GlobalExceptionMiddleware: SSE guard [if (Response.HasStarted) return;]
  ├─ RateLimiter: login (5/min), webhook-receiver (per WebhookOptions)
  ├─ Antiforgery: X-XSRF-TOKEN required on state-changing requests
  └─ Authorization: fallback requires auth [AllowAnonymous] on public routes
```

## Receiver Custom Response (New 2026-05-07)
- CustomResponse.Headers field stored as JSON string
- Receiver: deserializes headers, writes to Response.Headers
- Rate limiting: webhook-receiver policy applies → Authentication → Authorization
  ├─ GlobalExceptionMiddleware: catches exceptions, guards SSE with `if (Response.HasStarted) return;`
  ├─ RateLimiter: `login` policy (5/min), `webhook-receiver` policy (per WebhookOptions)
  ├─ Antiforgery: `X-XSRF-TOKEN` required on POST/PUT/DELETE
  └─ Authorization: fallback policy requires auth unless [AllowAnonymous]
```

## Receiver with Custom Response Headers (New 2026-05-07)

When token has CustomResponse set:
1. Headers field (JSON string) parsed via `JsonSerializer.Deserialize()`
2. Each header key/value written directly to `Response.Headers`
3. Response sent with custom status code + body + applied headers
4. Receiver rate limit: `webhook-receiver` policy (enables resilience)
→ Authentication → Authorization → Controllers
```

## Exception Handling (GlobalExceptionMiddleware)
```
OperationCanceledException (RequestAborted)  → silent swallow (normal SSE disconnect)
ValidationException                          → 422 Unprocessable Entity + field errors
BadHttpRequestException                      → 400 Bad Request or 413 Payload Too Large
Exception (unhandled)                        → 500 Internal Server Error (logged, not exposed)

SSE Safety: writes guarded by context.Response.HasStarted check
```

## Key Files
```
src/WebhookService.API/Program.cs                               (DI, middleware, EnableRetryOnFailure 3×2s)
src/WebhookService.API/Middleware/GlobalExceptionMiddleware.cs  (BadHttpRequestException handler, SSE guard)
src/WebhookService.API/Controllers/AuthController.cs           (login/logout/me)
src/WebhookService.API/Controllers/WebhookController.cs        (receiver: GetByTokenIncludingInactiveAsync, 410 Gone, catch IOException)
src/WebhookService.API/Controllers/SseController.cs            (SSE subscribe/stream)
src/WebhookService.API/Controllers/TokensController.cs         (CRUD + custom-response)
src/WebhookService.API/Controllers/RequestsController.cs       (paging, export, delete)
src/WebhookService.Application/Common/Behaviors/ValidationBehavior.cs
src/WebhookService.Application/Common/Behaviors/LoggingBehavior.cs
src/WebhookService.Infrastructure/Persistence/Repositories/WebhookRequestRepository.cs (ThenByDescending(r => r.Id) for determinism)
src/WebhookService.Infrastructure/Persistence/Configurations/WebhookRequestConfiguration.cs (ReceivedAt: datetimeoffset(7))
src/WebhookService.Infrastructure/Sse/SseNotifier.cs           (ConcurrentDictionary<Guid, Channel>)
src/WebhookService.Infrastructure/BackgroundServices/RetentionCleanupService.cs
```

## Options (validated at startup)
```
Webhook:BaseUrl          (required, must be non-empty — validator rejects blank)
                         Default in appsettings.json: "" (forces explicit config)
                         Dev default in appsettings.Development.json: http://localhost:8080
                         Production: set WEBHOOK_BASE_URL env var
Webhook:RetentionDays    — requests older than N days cleaned up
Webhook:MaxRequestSizeMb — Kestrel body size limit
Auth:Username            (required)
Auth:PasswordHash        (required, must start with $2 — BCrypt)
Auth:SessionHours        — cookie sliding expiry
```

## WebhookUrl Computation
`webhookUrl` is NOT stored in the database. Computed at read time in `WebhookTokenExtensions.ToDto(baseUrl)`.
Both `GetTokenQueryHandler` and `GetTokensQueryHandler` use `IOptions<WebhookOptions>` (not raw `IConfiguration`)
to access `BaseUrl` — consistent with `CreateTokenCommandHandler`.

## IDOR Protection
`GetRequestById`, `ExportRequest`, `DeleteRequest` all include `WHERE TokenId = @tokenId`

