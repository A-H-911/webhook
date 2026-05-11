<!-- Generated: 2026-05-11 | Verified: 310 unit + 26 arch + 59 integration tests; PATCH /note endpoint + SetRequestNoteCommand; GetRequestByIdQuery returns processingTimeMs + note fields; domain entity encapsulation (private setters + mutation methods); WebhookOptionsValidator moved to Hookbin.API.Options -->

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
src/Hookbin.API/Program.cs                               (DI: AddCoreInfrastructure + AddApiInfrastructure)
src/Hookbin.API/Middleware/GlobalExceptionMiddleware.cs  (BadHttpRequestException, SSE guard)
src/Hookbin.API/Controllers/AuthController.cs           (login/logout/me + ISessionRevocationStore)
src/Hookbin.API/Controllers/WebhookController.cs        (receiver: ITokenCache, IRequestQueuePublisher)
src/Hookbin.API/Controllers/SseController.cs            (SSE subscribe, 10-connection cap)
src/Hookbin.API/Controllers/TokensController.cs         (CRUD + custom-response)
src/Hookbin.API/Controllers/RequestsController.cs       (paging, export, delete)
src/Hookbin.API/Services/ISessionRevocationStore.cs
src/Hookbin.API/Services/RedisSessionRevocationStore.cs
```

## Key Files — Infrastructure
```
src/Hookbin.Infrastructure/DependencyInjection.cs       (AddCoreInfrastructure, AddApiInfrastructure, AddStreamWorkerInfrastructure, AddJobsWorkerInfrastructure)
src/Hookbin.Infrastructure/Redis/RedisStreamPublisher.cs (XADD webhook-requests)
src/Hookbin.Infrastructure/Redis/RedisTokenCache.cs      (IMemoryCache wrapper, 5-min sliding)
src/Hookbin.Infrastructure/Redis/RedisStreamConsumerService.cs (XREADGROUP, PEL recovery, XACK)
src/Hookbin.Infrastructure/Redis/RedisSseBridgeService.cs (SUBSCRIBE sse:* → SseNotifier)
src/Hookbin.Infrastructure/Sse/SseNotifier.cs            (Channel<SseEvent>, max 10/token)
src/Hookbin.Infrastructure/BackgroundServices/RetentionCleanupService.cs (24h PeriodicTimer)
src/Hookbin.Infrastructure/Persistence/ApplicationDbContext.cs
src/Hookbin.Infrastructure/Persistence/Repositories/WebhookRequestRepository.cs (ThenByDescending Id for determinism)
src/Hookbin.Application/Caching/ITokenCache.cs
src/Hookbin.Domain/Services/IRequestQueuePublisher.cs
src/Hookbin.Domain/Services/ISseNotifier.cs
```

## Key Files — Workers
```
src/Hookbin.StreamWorker/Program.cs   (AddCoreInfrastructure + AddStreamWorkerInfrastructure; Polly DB wait; /health/live + /health/ready)
src/Hookbin.JobsWorker/Program.cs     (AddCoreInfrastructure + AddJobsWorkerInfrastructure; Polly DB wait; SQL-only health check)
```

## Options (validated at startup)
```
Webhook:BaseUrl          (required) — used by API only; workers don't need it
Webhook:RetentionDays    — used by JobsWorker's RetentionCleanupService
Webhook:MaxRequestSizeMb — Kestrel body size limit (API only)
Auth:Username / PasswordHash / SessionHours — API only
HOOKBIN_WORKER_ID        — StreamWorker consumer identity; stable across restarts
```
⚠ `WebhookOptionsValidator` lives in `Hookbin.API.Options` (not Application) — its source file path
matches `src/Hookbin.API/Options/`. It implements `IValidateOptions<WebhookOptions>` from Application.

## Architecture Tests
`tests/Hookbin.ArchitectureTests/` — 26 rules enforced at build time:
```
Layers/LayerDependencyTests.cs          (8)  — layer isolation, no cycles
Conventions/CqrsConventionTests.cs      (5)  — sealed record commands, internal sealed handlers, validators
Conventions/RepositoryEntityConventionTests.cs (4) — repo interfaces in Domain, impls in Infrastructure
Conventions/ControllerMiddlewareConventionTests.cs (3) — public sealed controllers, middleware shape
Conventions/TestProjectConventionTests.cs (3) — FA version uniformity, sealed test classes
Structure/FolderNamespaceTests.cs        (3) — folder path matches CLR namespace
```
Run: `dotnet test tests/Hookbin.ArchitectureTests/`  (no Docker, ~1s)

## Request Note Command
```
PATCH /api/tokens/{tokenId}/requests/{id}/note  [FromBody] { note: string|null }
  → SetRequestNoteCommand(TokenId, RequestId, Note?) : IRequest<bool>
  → SetRequestNoteCommandValidator: Note max 2000 chars (null allowed = clear note)
  → IWebhookRequestRepository.UpdateNoteAsync → ExecuteUpdateAsync
  Returns 200 OK / 404 Not Found
```
Key files: `src/Hookbin.Application/Requests/Commands/SetRequestNote/`

## Domain Entity Mutation Methods
`WebhookToken` and `WebhookRequest` use `private set;` on mutable properties — callers use intent-named methods:
```
WebhookToken:
  Activate()                          → IsActive = true
  Deactivate()                        → IsActive = false
  UpdateDescription(string)           → Description = value
  SetCustomResponse(CustomResponse)   → CustomResponse = value
  ClearCustomResponse()               → CustomResponse = null

WebhookRequest:
  RecordProcessingTime(long ms)       → ProcessingTimeMs = Math.Max(0, ms)
```
EF Core reads/writes `private set;` properties via reflection — no mapping changes required.

## IDOR Protection
`GetRequestById`, `ExportRequest`, `DeleteRequest`, `SetRequestNote` all include `WHERE TokenId = @tokenId`

## WebhookUrl Computation
`webhookUrl` is NOT stored in DB. Computed at read time via `WebhookTokenExtensions.ToDto(baseUrl)`.
Both `GetTokenQueryHandler` and `GetTokensQueryHandler` use `IOptions<WebhookOptions>` for `BaseUrl`.
