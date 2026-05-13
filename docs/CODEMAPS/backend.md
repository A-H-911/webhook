<!-- Generated: 2026-05-13 | Files scanned: 59 src + 50 tests | Token estimate: ~950 -->

# Backend (.NET 10)

## Solution Layout
```
src/
  Hookbin.Domain/                 Entities, value objects, repository interfaces, ISseNotifier
  Hookbin.Application/            CQRS handlers (MediatR), DTOs, validators, behaviors, options
  Hookbin.Infrastructure/         EF Core (MSSQL), Redis adapters, SseNotifier, background services
  Hookbin.API/                    HTTP endpoints, middleware, options, services
  Hookbin.StreamWorker/           Drains Redis stream → SQL → SSE pub
  Hookbin.JobsWorker/             Retention cleanup
tools/
  RotatePassword/                 BCrypt hash generator CLI
```

## API Routes
```
AuthController              [Route("api/auth")]
  POST   /api/auth/login            (AllowAnonymous, login rate limiter)
  POST   /api/auth/logout
  GET    /api/auth/me

DashboardController         [Route("api/dashboard")]
  GET    /api/dashboard/metrics

InfoController              [Route("api")]
  GET    /api/version

TokensController            [Route("api/tokens")]
  GET    /api/tokens                                    → GetTokensQuery
  GET    /api/tokens/{id:guid}                          → GetTokenQuery
  POST   /api/tokens                                    → CreateTokenCommand
  PUT    /api/tokens/{id:guid}                          → UpdateTokenCommand
  DELETE /api/tokens/{id:guid}                          → DeleteTokenCommand
  PUT    /api/tokens/{id:guid}/custom-response          → SetCustomResponseCommand
  DELETE /api/tokens/{id:guid}/custom-response          → ResetCustomResponseCommand

RequestsController          [Route("api/tokens/{tokenId:guid}/requests")]
  GET    .../requests                                   → GetRequestsQuery
  GET    .../requests/{id:guid}                         → GetRequestByIdQuery
  GET    .../requests/{id:guid}/export                  → ExportRequestQuery
  PATCH  .../requests/{id:guid}/note                    → SetRequestNoteCommand
  DELETE .../requests                                   → ClearRequestsCommand
  DELETE .../requests/{id:guid}                         → DeleteRequestCommand

SseController
  GET    /api/tokens/{tokenId:guid}/sse                 → SseNotifier subscribe

WebhookController           [AllowAnonymous, Route("webhook/{token:guid}")]
  ALL (GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS)          → IRequestQueuePublisher.PublishAsync
```

## Middleware Pipeline (Program.cs)
```
ForwardedHeaders → GlobalExceptionMiddleware → CORS (dev only) → Static
  → Routing → RateLimiter → Authentication → Authorization → Antiforgery → MapControllers
```

## Domain Layer (`src/Hookbin.Domain/`)
| File | Type |
|---|---|
| `Entities/WebhookToken.cs` | Aggregate root; owns `CustomResponse` value object |
| `Entities/WebhookRequest.cs` | Request entity (`[JsonInclude]` on private-set `ProcessingTimeMs`) |
| `Entities/DashboardMetrics.cs`, `TokenPageRow.cs` | Read DTOs |
| `ValueObjects/CustomResponse.cs` | Owned entity — `StatusCode`, `ContentType`, `Body`, `Headers` (raw JSON string) |
| `Repositories/IWebhookTokenRepository.cs` | Read + write contract |
| `Repositories/IWebhookRequestRepository.cs` | Read + write contract |
| `Services/ISseNotifier.cs` | SSE fan-out interface |

## Application Layer — CQRS Map (MediatR)
**Commands** — handlers are `internal sealed`, records are `public sealed record`:

| Folder | Command | Validator | Cache Invalidation |
|---|---|---|---|
| `Tokens/Commands/CreateToken/` | `CreateTokenCommand` | yes (name ≤80) | n/a |
| `Tokens/Commands/UpdateToken/` | `UpdateTokenCommand` | yes | `cache.Remove(tokenId)` |
| `Tokens/Commands/DeleteToken/` | `DeleteTokenCommand` | - | `cache.Remove(tokenId)` |
| `Tokens/Commands/SetCustomResponse/` | `SetCustomResponseCommand` | yes (Headers JSON object) | `cache.Remove(tokenId)` |
| `Tokens/Commands/ResetCustomResponse/` | `ResetCustomResponseCommand` | - | `cache.Remove(tokenId)` |
| `Requests/Commands/SetRequestNote/` | `SetRequestNoteCommand` | yes (≤2000 chars) | n/a |
| `Requests/Commands/DeleteRequest/` | `DeleteRequestCommand` | - | n/a (IDOR guarded: `WHERE TokenId=`) |
| `Requests/Commands/ClearRequests/` | `ClearRequestsCommand` | - | n/a |

**Queries:**
| Folder | Query | Returns |
|---|---|---|
| `Tokens/Queries/GetToken/` | `GetTokenQuery` | `TokenDto?` |
| `Tokens/Queries/GetTokens/` | `GetTokensQuery` | `Page<TokenListItemDto>` |
| `Requests/Queries/GetRequests/` | `GetRequestsQuery` | `Page<RequestDto>` |
| `Requests/Queries/GetRequestById/` | `GetRequestByIdQuery` | `WebhookRequestDetailDto?` |
| `Requests/Queries/ExportRequest/` | `ExportRequestQuery` | byte[] JSON |
| `Dashboard/Queries/GetDashboardMetrics/` | `GetDashboardMetricsQuery` | `DashboardMetricsDto` |

## Pipeline Behaviors (`Application/Common/Behaviors/`)
- `ValidationBehavior<TReq,TResp>` → auto-discovers `AbstractValidator<TReq>`, throws `ValidationException` (→ HTTP 422)
- `LoggingBehavior<TReq,TResp>` → structured request/response/timing with Serilog

## Infrastructure (`src/Hookbin.Infrastructure/`)
```
Persistence/
  ApplicationDbContext.cs
  Configurations/  WebhookTokenConfiguration.cs, WebhookRequestConfiguration.cs
  Repositories/    WebhookTokenRepository.cs, WebhookRequestRepository.cs   (.AsNoTracking on reads)
Redis/
  RedisStreamPublisher.cs         (XADD webhook-requests)
  RedisStreamConsumerService.cs   (XREADGROUP + PEL recovery + XACK)
  RedisSseBridgeService.cs        (SUBSCRIBE sse:* → SseNotifier.NotifyAsync)  ← API only
  RedisTokenCache.cs              (5min sliding, ICacheStore behind ITokenCache)
  RedisSessionRevocationStore.cs  (logout = cluster-wide invalidation)
Sse/SseNotifier.cs                (ConcurrentDictionary<TokenId, Channel<SseEvent>>, DropOldest)
BackgroundServices/RetentionCleanupService.cs    (PeriodicTimer 24h, IServiceScopeFactory)
GeoIp/MaxMindGeoIpService.cs
Migrations/                       (5 migrations, see data.md)
```

## API Project (`src/Hookbin.API/`)
| Subsystem | Files |
|---|---|
| Entry / DI / pipeline | `Program.cs` |
| Controllers | `Controllers/AuthController.cs`, `TokensController.cs`, `RequestsController.cs`, `SseController.cs`, `WebhookController.cs`, `DashboardController.cs`, `InfoController.cs` |
| Middleware | `Middleware/GlobalExceptionMiddleware.cs` (HTTP-status mapping + `HasStarted` SSE guard + 401 redirect-suppress) |
| Options | `Options/WebhookOptions.cs`, `AuthOptions.cs`, plus `IValidateOptions` validators |
| Services | `Services/SessionRevocationStore.cs` |

## Worker Projects
- `src/Hookbin.StreamWorker/Program.cs` → `AddCoreInfrastructure + AddStreamWorkerInfrastructure`; Polly readiness; `/health/live` + `/health/ready`
- `src/Hookbin.JobsWorker/Program.cs` → `AddCoreInfrastructure + AddJobsWorkerInfrastructure`; SQL-only readiness

## HTTP Status Map (GlobalExceptionMiddleware)
| Exception | Status |
|---|---|
| `FluentValidation.ValidationException` | 422 |
| `BadHttpRequestException` | `ex.StatusCode` (400/413) |
| `OperationCanceledException` + `RequestAborted` | swallowed (SSE disconnect) |
| `NotFoundException` | 404 |
| Anything else | 500 |

## Test Projects (`tests/`)
| Project | Tests | Tool |
|---|---:|---|
| `Hookbin.UnitTests` | 377 | xUnit + NSubstitute + FluentAssertions |
| `Hookbin.IntegrationTests` | 85 | xUnit + WebAppFactory + Testcontainers (MSSQL + Redis) |
| `Hookbin.ArchitectureTests` | 47 | ArchUnitNET + NetArchTest |
| `Hookbin.E2ETests` | 64 | xUnit + Playwright + shared `DashboardE2EFixture` |

Audit coverage / mutation scores: `docs/AUDIT/BASELINE.md`.
