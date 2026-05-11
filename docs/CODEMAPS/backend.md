<!-- Generated: 2026-05-11 | Updated: PATCH /note endpoint + SetRequestNoteCommand; GetRequestByIdQuery now returns processingTimeMs + note fields -->

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
GET    /api/tokens/{tokenId}/requests/{id}        → GetRequestByIdQuery  (returns processingTimeMs + note)
GET    /api/tokens/{tokenId}/requests/{id}/export → ExportRequestQuery → File(json)
DELETE /api/tokens/{tokenId}/requests             → ClearRequestsCommand
DELETE /api/tokens/{tokenId}/requests/{id}        → DeleteRequestCommand
PATCH  /api/tokens/{tokenId}/requests/{id}/note   → SetRequestNoteCommand → SetRequestNoteCommandHandler
```

### Webhook Receiver (AllowAnonymous, rate-limited)
```
*/webhook/{token:guid}   → WebhookController.Receive (GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS)
  [EnableRateLimiting("webhook-receiver")]
  ├─ ITokenCache.GetOrLoadAsync → DB fallback (includes inactive tokens)
  ├─ Read body: try/catch IOException → BadHttpRequestException
  ├─ IRequestQueuePublisher.PublishAsync → XADD webhook-requests (Redis stream)
  ├─ Active token:   return CustomResponse (or 200)
  ├─ Inactive token: return 410 Gone (audit persisted by StreamWorker)
  └─ Rate limited:   429 Too Many Requests
```

### Health (AllowAnonymous)
```
GET /health/live   → no checks (API + StreamWorker + JobsWorker)
GET /health/ready  → SqlServer ping [+Redis ping for StreamWorker]
```

## Middleware Chain (ordered)
```
ForwardedHeaders → GlobalExceptionMiddleware → CORS → RateLimiter → Authentication → Antiforgery → Authorization
  ├─ GlobalExceptionMiddleware: SSE guard [if (Response.HasStarted) return;]
  ├─ RateLimiter: login (5/min), webhook-receiver (per WebhookOptions)
  ├─ Antiforgery: X-XSRF-TOKEN required on POST/PUT/DELETE
  └─ Authorization: fallback requires auth; [AllowAnonymous] on public routes
```

## Exception Handling (GlobalExceptionMiddleware)
```
OperationCanceledException (RequestAborted)  → silent swallow (normal SSE disconnect)
ValidationException                          → 422 Unprocessable Entity + field errors
BadHttpRequestException                      → status code from ex.StatusCode (400 or 413)
Exception (unhandled)                        → 500 Internal Server Error (logged, not exposed)

SSE Safety: writes guarded by context.Response.HasStarted check
```

## Service Interfaces
```
IRequestQueuePublisher  (Domain)              → RedisStreamPublisher (XADD webhook-requests)
ITokenCache             (Application/Caching) → RedisTokenCache (IMemoryCache, 5-min sliding)
ISseNotifier            (Domain)              → SseNotifier (ConcurrentDictionary<Guid, Channel<SseEvent>>)
ISessionRevocationStore (API/Services)        → RedisSessionRevocationStore (in-memory for now)
```

## Token Command Cache Invalidation
All four mutating token commands call `ITokenCache.Remove(tokenId)` after DB write:
```
DeleteTokenCommand → DeleteTokenCommandHandler → repo.DeleteAsync → cache.Remove
UpdateTokenCommand → UpdateTokenCommandHandler → repo.UpdateAsync → cache.Remove
SetCustomResponseCommand → ... → cache.Remove
ResetCustomResponseCommand → ... → cache.Remove
```

## Key Files — API
```
src/WebhookService.API/Program.cs                               (DI: AddCoreInfrastructure + AddApiInfrastructure)
src/WebhookService.API/Middleware/GlobalExceptionMiddleware.cs  (BadHttpRequestException, SSE guard)
src/WebhookService.API/Controllers/AuthController.cs           (login/logout/me + ISessionRevocationStore)
src/WebhookService.API/Controllers/WebhookController.cs        (receiver: ITokenCache, IRequestQueuePublisher)
src/WebhookService.API/Controllers/SseController.cs            (SSE subscribe, 10-connection cap)
src/WebhookService.API/Controllers/TokensController.cs         (CRUD + custom-response)
src/WebhookService.API/Controllers/RequestsController.cs       (paging, export, delete)
src/WebhookService.API/Services/ISessionRevocationStore.cs
src/WebhookService.API/Services/RedisSessionRevocationStore.cs
```

## Key Files — Infrastructure
```
src/WebhookService.Infrastructure/DependencyInjection.cs       (AddCoreInfrastructure, AddApiInfrastructure, AddStreamWorkerInfrastructure, AddJobsWorkerInfrastructure)
src/WebhookService.Infrastructure/Redis/RedisStreamPublisher.cs (XADD webhook-requests)
src/WebhookService.Infrastructure/Redis/RedisTokenCache.cs      (IMemoryCache wrapper, 5-min sliding)
src/WebhookService.Infrastructure/Redis/RedisStreamConsumerService.cs (XREADGROUP, PEL recovery, XACK)
src/WebhookService.Infrastructure/Redis/RedisSseBridgeService.cs (SUBSCRIBE sse:* → SseNotifier)
src/WebhookService.Infrastructure/Sse/SseNotifier.cs            (Channel<SseEvent>, max 10/token)
src/WebhookService.Infrastructure/BackgroundServices/RetentionCleanupService.cs (24h PeriodicTimer)
src/WebhookService.Infrastructure/Persistence/ApplicationDbContext.cs
src/WebhookService.Infrastructure/Persistence/Repositories/WebhookRequestRepository.cs (ThenByDescending Id for determinism)
src/WebhookService.Application/Caching/ITokenCache.cs
src/WebhookService.Domain/Services/IRequestQueuePublisher.cs
src/WebhookService.Domain/Services/ISseNotifier.cs
```

## Key Files — Workers
```
src/WebhookService.StreamWorker/Program.cs   (AddCoreInfrastructure + AddStreamWorkerInfrastructure; Polly DB wait; /health/live + /health/ready)
src/WebhookService.JobsWorker/Program.cs     (AddCoreInfrastructure + AddJobsWorkerInfrastructure; Polly DB wait; SQL-only health check)
```

## Options (validated at startup)
```
Webhook:BaseUrl          (required) — used by API only; workers don't need it
Webhook:RetentionDays    — used by JobsWorker's RetentionCleanupService
Webhook:MaxRequestSizeMb — Kestrel body size limit (API only)
Auth:Username / PasswordHash / SessionHours — API only
WEBHOOK_WORKER_ID        — StreamWorker consumer identity; stable across restarts
```

## Request Note Command
```
PATCH /api/tokens/{tokenId}/requests/{id}/note  [FromBody] { note: string|null }
  → SetRequestNoteCommand(TokenId, RequestId, Note?) : IRequest<bool>
  → SetRequestNoteCommandValidator: Note max 2000 chars (null allowed = clear note)
  → IWebhookRequestRepository.UpdateNoteAsync → ExecuteUpdateAsync
  Returns 200 OK / 404 Not Found
```
Key files: `src/WebhookService.Application/Requests/Commands/SetRequestNote/`

## IDOR Protection
`GetRequestById`, `ExportRequest`, `DeleteRequest`, `SetRequestNote` all include `WHERE TokenId = @tokenId`

## WebhookUrl Computation
`webhookUrl` is NOT stored in DB. Computed at read time via `WebhookTokenExtensions.ToDto(baseUrl)`.
Both `GetTokenQueryHandler` and `GetTokensQueryHandler` use `IOptions<WebhookOptions>` for `BaseUrl`.
