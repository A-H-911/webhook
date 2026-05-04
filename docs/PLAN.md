# Webhook Service — Implementation Plan

> **Status:** Implementation Complete — Phases 1–11 done; full solution builds 0 errors, 20 unit tests pass, integration tests (Testcontainers.MsSql) and E2E tests (Playwright) scaffolded and compile clean
> **Last updated:** 2026-05-04
> **Stack:** .NET 10 · Angular (latest) · MSSQL 2022 · SSE · SEQ · Docker Compose · Clean Architecture

---

## Revision History

| Version | Date | Summary |
|---------|------|---------|
| v1 | 2026-05-04 | Initial plan |
| v2 | 2026-05-04 | Devil's advocate review — 5 blocking flaws fixed, 5 high issues resolved, 8 medium addressed |
| v3 | 2026-05-04 | Hangfire removed. 6→4 Docker services. SSE notify is direct in-process call. Retention uses BackgroundService. 12→11 phases. |
| v4 | 2026-05-04 | Second devil's advocate review — 4 new blocking + 5 high + 8 medium issues found and fixed (IP forwarding, BackgroundService crash, NullSseNotifier, body reading, CORS, port config, Host header, SSE race, options validation) |

---

## All Issues Found and Fixed

### Blocking

| # | Issue | Fix Applied |
|---|-------|-------------|
| B1 | ~~Hangfire job carried full request body~~ | Resolved by removal (v3) |
| B2 | Token cache not invalidated on CustomResponse update | Explicit `_memoryCache.Remove()` in SetCustomResponse AND ResetCustomResponse handlers |
| B3 | MSSQL Docker `init.sql` not supported by MSSQL image | Custom Dockerfile + `entrypoint.sh` polling loop + `sqlcmd` |
| B4 | Request body stream consumed by middleware before controller | `Request.EnableBuffering()` as first line in WebhookController |
| B5 | Kestrel `MaxRequestBodySize` not set from env var | `ConfigureKestrel()` in Program.cs + `[RequestSizeLimit]` attribute |
| BN1 | **All captured IPs are Nginx's Docker internal IP, not the real client IP** | Nginx passes `X-Real-IP` + `X-Forwarded-For` on all proxy locations. API calls `UseForwardedHeaders()` early in pipeline. `WebhookController` reads from forwarded headers. |
| BN2 | **`RetentionCleanupService` permanently stops on first DB error** | Try/catch wraps entire cleanup body; errors are logged, service continues on next tick |
| BN3 | **Phase 2 calls `ISseNotifier.NotifyAsync()` but no implementation exists until Phase 3** | `NullSseNotifier` (no-op) registered in Phase 1 DI; replaced by real `SseNotifier` in Phase 3 |
| BN4 | **Body silently empty for requests without `Content-Length` header** | Always attempt to read body (reset position after `EnableBuffering`); condition no longer gates on `ContentLength > 0` |

### High Priority

| # | Issue | Fix Applied |
|---|-------|-------------|
| H1 | ~~Worker starts before API ready~~ | Resolved by removal (v3) |
| H2 | ~~Hangfire job retry idempotency~~ | Resolved by removal (v3) |
| H3 | ~~Hangfire dashboard read-only~~ | Resolved by removal (v3) |
| H4 | List endpoint returned full body — 200MB responses possible | `WebhookRequestSummaryDto` (no body) for list; `WebhookRequestDetailDto` (with body) for detail |
| H5 | CORS not configured for Angular dev | CORS from `Cors__AllowedOrigins` env var; dev override adds port 4200 |
| HN1 | **`"".Split(',')` produces `[""]` — empty string added as CORS origin** | Split uses `StringSplitOptions.RemoveEmptyEntries \| TrimEntries`; entire CORS block skipped if string is empty/whitespace |
| HN2 | **API container port unspecified — Kestrel port is image-version-dependent** | `ASPNETCORE_HTTP_PORTS: "8080"` added explicitly to docker-compose API service |
| HN3 | **`proxy_set_header Host` missing — generated webhookUrl is `http://api:8080/...`** | Nginx passes `Host $host` on all proxy locations. `WEBHOOK_BASE_URL` made required at startup validation. |
| HN4 | **SSE connection count check is not atomic (TOCTOU race)** | Connection tracking uses `ConcurrentDictionary` with `AddOrUpdate` atomic semantics; count checked and incremented in one operation |
| HN5 | **`WebhookOptions` not validated at startup — silent misconfiguration** | `IValidateOptions<WebhookOptions>` implementation; startup fails fast on invalid values |

### Medium

| # | Issue | Action |
|---|-------|--------|
| M1 | SSE stream hangs when token deleted | `token-deleted` SSE event + `Channel.Writer.Complete()` on delete |
| M2 | No SSE connection limit per token | Max 10 concurrent connections per token → 429 if exceeded |
| M3 | Binary payloads corrupt in NVARCHAR(MAX) | Base64-encode; `IsBodyBase64` flag |
| M4 | Search resets pagination | Angular resets to page 1 when search term changes |
| M5 | No `pageSize` ceiling | FluentValidation enforces `pageSize` ≤ 100 |
| M6 | ~~Worker had no HttpClient~~ | Resolved by removal (v3) |
| M7 | Tests written entirely after code | Unit tests written alongside each backend phase |
| M8 | Redis migration note | `ISseNotifier` interface unchanged; implementation replaced when Redis added |
| MN1 | **`NotifyAsync` called without try/catch in WebhookController** | Wrapped in try/catch; exception logged as warning; 200 still returned to caller (request is already durable) |
| MN2 | **`RetentionCleanupService` injects scoped `DbContext` into singleton** | Uses `IServiceScopeFactory`; creates a new scope per cleanup tick |
| MN3 | **Token cache: manual get/set, no stampede protection** | `GetOrCreateAsync` with sliding expiry; null result (token not found) is NOT cached — `_cache.Remove(key)` guards this |
| MN4 | **`Path` and `QueryString` are unbounded NVARCHAR(MAX) columns** | EF config sets `Path` max 2048, `QueryString` max 4096 |
| MN5 | **`SseEvent` record never defined in solution structure** | Added to `Domain/Services/SseEvent.cs` |
| MN6 | **`GetTokensQuery` filter not stated — soft-deleted tokens visible?** | Handler explicitly filters `WHERE IsActive = 1`; documented |
| MN7 | **E2E tests require Docker Compose v2 (`--wait` flag) — not documented** | Prerequisite noted in Phase 11; `docker compose version` check in CI setup |
| MN8 | **No local dev DB guidance for Phases 1–6** | Standalone `docker run` SQL Server command added to Phase 1 |

---

## Table of Contents

1. [Confirmed Design Decisions](#1-confirmed-design-decisions)
2. [High-Level Design](#2-high-level-design)
3. [Low-Level Design](#3-low-level-design)
4. [URL Generation and Request Flow](#4-url-generation-and-request-flow)
5. [Solution Structure](#5-solution-structure)
6. [Docker Compose](#6-docker-compose)
7. [Technology Stack](#7-technology-stack)
8. [Implementation Phases](#8-implementation-phases)
9. [Testing Strategy](#9-testing-strategy)
10. [Known Limitations](#10-known-limitations)
11. [Future-Proofing Notes](#11-future-proofing-notes)
12. [Access Points](#12-access-points)

---

## 1. Confirmed Design Decisions

| # | Topic | Decision |
|---|-------|----------|
| 1 | Real-time delivery | SSE (Server-Sent Events) — one-way push per token stream |
| 2 | Data retention | Configurable via `RETENTION_DAYS` env var; `BackgroundService` cleans up daily |
| 3 | Custom response | Static per URL (status code + headers + body) |
| 4 | Request replay | No — capture and inspect only |
| 5 | Token format | UUID — `/webhook/{guid}` |
| 6 | URL grouping | Flat list — no grouping |
| 7 | Search | LIKE-based across headers + body; max pageSize=100 |
| 8 | Request size limit | Configurable via `MAX_REQUEST_SIZE_MB`; enforced in Kestrel AND middleware |
| 9 | Export | JSON per-request download (`WebhookRequestDetailDto` serialised to JSON) |
| 10 | Background jobs | No Hangfire — `BackgroundService` + `PeriodicTimer(24h)` in API |
| 11 | SSE notify | Direct in-process call from `WebhookController` via `ISseNotifier.NotifyAsync()` after INSERT |
| 12 | Database | Single `WebhookDb` on SQL Server 2022 Developer Edition |
| 13 | SEQ | Container in docker-compose |
| 14 | MSSQL init | Custom Docker image with `entrypoint.sh` + `sqlcmd` |
| 15 | Scale target | < 100 webhook URLs, small team, single API instance |
| 16 | Frontend | Separate Nginx container serving Angular build |
| 17 | Testing | Unit (alongside code) + Integration + E2E (Playwright) |
| 18 | Redis | Future upgrade — SSE notify only (multi-instance API scenario) |
| 19 | Binary payloads | Base64-encoded; `IsBodyBase64` flag on entity |
| 20 | SSE notify timing | `TryWrite` to bounded Channel — O(1), non-blocking; wrapped in try/catch |
| 21 | IP capture | `UseForwardedHeaders()` in API; Nginx passes `X-Real-IP` + `X-Forwarded-For` |
| 22 | `WebhookOptions` | Validated at startup via `IValidateOptions<WebhookOptions>`; `WEBHOOK_BASE_URL` is required |

---

## 2. High-Level Design

### 2.1 System Context

```
External callers ──────────────────► ANY /webhook/{uuid}
  (real IP forwarded via X-Forwarded-For)         │
                               ┌──────────────────▼─────────────────────────┐
                               │    WebhookService.API  (:8080)              │
                               │  UseForwardedHeaders() → real IP captured   │
                               │  Token CRUD · SSE stream · Webhook receiver │
                               │  INSERT to DB → ISseNotifier.NotifyAsync()  │
                               │  RetentionCleanupService (BackgroundService) │
                               └────────────────────────────────────────────┘

  ┌─────────────────────────────────────┐
  │  sqlserver (custom image)           │
  │  └── WebhookDb  (tokens, requests)  │
  └─────────────────────────────────────┘

  ┌──────────────────┐    ┌──────────────────────────────────────────┐
  │  SEQ (:5342 UI)  │    │  Nginx (:80) + Angular SPA               │
  │                  │    │  passes Host, X-Forwarded-For headers    │
  └──────────────────┘    └──────────────────────────────────────────┘
```

**4 Docker services:** `sqlserver`, `seq`, `api`, `frontend`

### 2.2 Core Data Flows

**Flow A — Incoming Webhook Request**

```
POST /webhook/{uuid}
  → Nginx proxies to API
      proxy_set_header Host $host
      proxy_set_header X-Real-IP $remote_addr
      proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for
  → API: UseForwardedHeaders() resolves real client IP from X-Forwarded-For
  → WebhookController: EnableBuffering() — first line
  → Read body unconditionally (don't gate on ContentLength)
  → Size check: body bytes > MaxRequestSizeMb → 413
  → Token lookup: GetOrCreateAsync(cacheKey, 60s sliding)
      Cache miss → SELECT FROM WebhookTokens WHERE Token=@t AND IsActive=1
      Not found → remove key from cache (don't cache null), return 404
  → Generate RequestId = Guid.NewGuid()
  → Detect binary Content-Type → Base64-encode body if needed
  → INSERT WebhookRequest to WebhookDb (~2–5ms)
  → try { await ISseNotifier.NotifyAsync(tokenId, summaryDto) }
    catch { log warning — request already durable, SSE is best-effort }
  → Return custom response to caller (~5–10ms total)
```

**Flow B — View Requests in SPA**

```
User opens token detail page
  → GET /api/tokens/{uuid}/requests?page=1&pageSize=20   (SummaryDto[], no body)
  → GET /api/events/{uuid}                                (SSE subscribe)
      Nginx: proxy_buffering off; proxy_read_timeout 3600s; chunked_transfer_encoding on
      API: connection count check (atomic) → 429 if >= 10
      keepalive: "comment: ping" every 15s via background timer
  → New request arrives → SSE event delivered immediately (in-process)
  → User clicks row → GET /api/tokens/{uuid}/requests/{reqId}  (DetailDto with body)
```

**Flow C — Configure Custom Response**

```
User edits response in SPA
  → PUT /api/tokens/{uuid}/response
  → SetCustomResponseCommand handler:
      UPDATE WebhookTokens SET CustomResponse...
      _memoryCache.Remove(tokenCacheKey)   ← explicit invalidation
  → Next incoming request uses new response immediately
```

Cache also invalidated in `ResetCustomResponseCommand` and `DeleteTokenCommand`.

**Flow D — Token Deleted While SSE Connected**

```
User deletes token
  → DeleteTokenCommand handler:
      soft-delete token (IsActive=false)
      hard-delete all requests for token
      _memoryCache.Remove(tokenCacheKey)
      _sseNotifier.NotifyTokenDeleted(tokenId)
  → SseNotifier: writes "event: token-deleted" to each channel, then Channel.Writer.Complete()
  → SPA EventSource receives "token-deleted" → router.navigate(['/'])
```

**Flow E — Retention Cleanup**

```
RetentionCleanupService.ExecuteAsync (BackgroundService)
  → PeriodicTimer(24h) — first tick is 24h after startup
  → Creates new IServiceScope per tick (IServiceScopeFactory)
  → try:
      if (RetentionDays <= 0) skip   ← keep forever
      cutoff = UtcNow - RetentionDays
      count = repository.DeleteOlderThanAsync(cutoff)
      log: "Retention cleanup deleted {Count} records"
    catch (Exception):
      log error — service continues on next tick (does NOT stop)
```

---

## 3. Low-Level Design

### 3.1 Domain Entities

**WebhookToken**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK — internal |
| Token | Guid | Unique, indexed — public URL segment |
| Description | string? | Max 200 chars |
| CreatedAt | DateTimeOffset | UTC |
| IsActive | bool | Soft-delete flag |
| CustomResponse | CustomResponse? | EF Core owned entity (nullable) |

**CustomResponse** (owned value object, columns in WebhookTokens table)

| Field | Type | Default |
|-------|------|---------|
| StatusCode | int | 200 |
| ContentType | string | text/plain |
| Body | string? | null |
| Headers | string | JSON `{}` |

EF config: `.OwnsOne(t => t.CustomResponse, cr => { cr.Property(...).IsRequired(false); })` — nullable owned entity requires explicit `.IsRequired(false)` in EF Core 7+.

**WebhookRequest**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK — generated by API before INSERT |
| TokenId | Guid | FK → WebhookToken.Id, indexed |
| ReceivedAt | DateTimeOffset | Indexed — retention + date filter |
| Method | string | Max 10 chars |
| Path | string | **Max 2048 chars** — set in EF config |
| QueryString | string? | **Max 4096 chars** — set in EF config |
| Headers | string | NVARCHAR(MAX); JSON |
| Body | string? | NVARCHAR(MAX); Base64 if binary |
| IsBodyBase64 | bool | |
| ContentType | string? | Max 256 chars |
| IpAddress | string | Real client IP (from X-Forwarded-For); max 45 chars (IPv6) |
| UserAgent | string? | Max 512 chars |
| SizeBytes | long | |

**Index strategy:**
- `WebhookToken.Token` — unique index
- `WebhookRequest.TokenId` — non-clustered
- `WebhookRequest.ReceivedAt` — non-clustered (retention cleanup + date filter)
- Search: `LIKE '%term%'` on `Headers` + `Body` — full scan within a token's rows (acceptable at < 100 URL scale)

### 3.2 DTOs

**WebhookRequestSummaryDto** — list responses, NO body field

```
{ Id, TokenId, Method, Path, ReceivedAt, ContentType, SizeBytes, IpAddress }
```

**WebhookRequestDetailDto** — single-item GET, includes body

```
{ Id, TokenId, Method, Path, QueryString, ReceivedAt, ContentType,
  Headers (Dictionary<string,string>), Body, IsBodyBase64, SizeBytes, IpAddress, UserAgent }
```

**WebhookTokenDto**

```
{ Id, Token, WebhookUrl, Description, CreatedAt, IsActive, CustomResponse? }
```

### 3.3 API Contract

```
# Token Management
POST   /api/tokens                               → 201  WebhookTokenDto
GET    /api/tokens                               → 200  WebhookTokenDto[]   (IsActive=true only)
GET    /api/tokens/{uuid}                        → 200  WebhookTokenDto
DELETE /api/tokens/{uuid}                        → 204
PUT    /api/tokens/{uuid}/response               → 200  WebhookTokenDto
DELETE /api/tokens/{uuid}/response               → 204  (reset to 200 OK defaults)

# Request Management
GET    /api/tokens/{uuid}/requests               → 200  PagedResult<WebhookRequestSummaryDto>
                                                         { items, page, pageSize, total }
                                                         ?page=1&pageSize=20&search=foo
                                                         pageSize capped at 100
DELETE /api/tokens/{uuid}/requests               → 204  (clear all for token)
GET    /api/tokens/{uuid}/requests/{reqId}       → 200  WebhookRequestDetailDto
DELETE /api/tokens/{uuid}/requests/{reqId}       → 204
GET    /api/tokens/{uuid}/requests/{reqId}/export → 200  Content-Type: application/json
                                                          Content-Disposition: attachment; filename="request-{id}.json"

# Webhook Receiver (any HTTP method)
ANY    /webhook/{uuid}                           → custom response or 200 OK

# SSE Stream
GET    /api/events/{uuid}                        → 200  Content-Type: text/event-stream
                                                         429 if >= 10 concurrent connections for this token

# Health
GET    /health/live                              → 200  always (liveness — no dependency checks)
GET    /health/ready                             → 200  if WebhookDb reachable; 503 otherwise
```

**SSE event shapes:**

```
event: new-request
data: {"id":"...","method":"POST","path":"/webhook/...","receivedAt":"...","sizeBytes":342,"ipAddress":"1.2.3.4"}

event: token-deleted
data: {}

comment: ping                      (every 15s keepalive — keeps Nginx proxy alive)
retry: 3000                        (tells EventSource to reconnect after 3s)
```

### 3.4 SseEvent Record

```csharp
// Domain/Services/SseEvent.cs
public sealed record SseEvent(string EventName, string Data);
```

Defined in Domain (used by the `ISseNotifier` interface in the same layer).

### 3.5 SseNotifier Design

```csharp
// Domain/Services/ISseNotifier.cs
public interface ISseNotifier
{
    IAsyncEnumerable<SseEvent> SubscribeAsync(Guid tokenId, CancellationToken ct);
    Task NotifyAsync(Guid tokenId, WebhookRequestSummaryDto dto);
    void NotifyTokenDeleted(Guid tokenId);
}

// Infrastructure/Sse/NullSseNotifier.cs  — Phase 1 & 2 stub
internal sealed class NullSseNotifier : ISseNotifier
{
    public IAsyncEnumerable<SseEvent> SubscribeAsync(Guid tokenId, CancellationToken ct)
        => AsyncEnumerable.Empty<SseEvent>();
    public Task NotifyAsync(Guid tokenId, WebhookRequestSummaryDto dto) => Task.CompletedTask;
    public void NotifyTokenDeleted(Guid tokenId) { }
}

// Infrastructure/Sse/SseNotifier.cs  — Phase 3 (replaces NullSseNotifier in DI)
// State: ConcurrentDictionary<Guid tokenId, ConcurrentDictionary<Guid channelId, Channel<SseEvent>>>
// Connection count: atomic — stored as count in above outer dict; AddOrUpdate atomically checks+adds
// Subscribe:
//   1. Atomic check: if count >= 10 → throw TooManyConnectionsException (caught by EventsController → 429)
//   2. channelId = Guid.NewGuid()
//   3. Create bounded Channel<SseEvent>(capacity: 50)
//   4. AddOrUpdate to outer dict
//   5. yield return each item; on CancellationToken cancelled → remove channel from dict
// NotifyAsync: foreach channel in tokenId's dict → TryWrite (non-blocking; silently drops if full)
// NotifyTokenDeleted: write token-deleted SseEvent; Channel.Writer.Complete() each; remove from dict
```

**Atomic connection counting:** Use `ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<SseEvent>>>`. The outer dict's `Count` property on the inner dict is read atomically under a `lock(lockObj)` per tokenId when adding, preventing the TOCTOU race.

### 3.6 Application Layer (CQRS with MediatR)

**Token Commands**
- `CreateTokenCommand` / Handler — generates UUIDs, builds `webhookUrl` from `WEBHOOK_BASE_URL`, INSERT
- `DeleteTokenCommand` / Handler — soft-delete token, hard-delete requests, invalidate cache, `NotifyTokenDeleted`
- `SetCustomResponseCommand` / Handler — UPDATE, **invalidate cache**
- `ResetCustomResponseCommand` / Handler — clear CustomResponse fields, **invalidate cache**

**Request Commands**
- `DeleteRequestCommand` / Handler
- `ClearRequestsCommand` / Handler

**Token Queries**
- `GetTokensQuery` / Handler — **`WHERE IsActive = 1`** — soft-deleted tokens never returned
- `GetTokenByIdQuery` / Handler — returns 404 if not found or `IsActive = 0`

**Request Queries**
- `GetRequestsQuery` — `PagedResult<WebhookRequestSummaryDto>`; LIKE search; `pageSize` ≤ 100 (two DB queries: COUNT + paged SELECT)
- `GetRequestByIdQuery` — `WebhookRequestDetailDto`
- `ExportRequestQuery` — serialises `WebhookRequestDetailDto` to JSON bytes; filename `request-{id}.json`

**Pipeline Behaviors**
- `LoggingBehavior` — command/query name + duration; **never logs payloads** (may contain secrets)
- `ValidationBehavior` — FluentValidation before handler; maps `ValidationException` → 400

### 3.7 WebhookOptions and Startup Validation

```csharp
// API/Options/WebhookOptions.cs
public sealed class WebhookOptions
{
    [Required, Url]
    public string BaseUrl { get; init; } = string.Empty;   // REQUIRED — no fallback

    [Range(0, 365)]
    public int RetentionDays { get; init; } = 7;           // 0 = keep forever

    [Range(1, 100)]
    public int MaxRequestSizeMb { get; init; } = 5;
}

// API/Options/WebhookOptionsValidator.cs
public sealed class WebhookOptionsValidator : IValidateOptions<WebhookOptions>
{
    public ValidateOptionsResult Validate(string? name, WebhookOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return ValidateOptionsResult.Fail("WEBHOOK_BASE_URL is required.");
        if (options.MaxRequestSizeMb < 1 || options.MaxRequestSizeMb > 100)
            return ValidateOptionsResult.Fail("MAX_REQUEST_SIZE_MB must be 1–100.");
        return ValidateOptionsResult.Success;
    }
}
```

Registered with `.ValidateOnStart()` — application fails to start if any option is invalid.

### 3.8 Request Body Handling (fixed)

```csharp
// WebhookController — first lines
HttpContext.Request.EnableBuffering();   // makes body seekable; must be first

// Always read body — do NOT gate on ContentLength (chunked / no Content-Length requests)
Request.Body.Position = 0;
var isBinary = IsBinaryContentType(Request.ContentType);
string body;
bool isBase64;

if (isBinary)
{
    using var ms = new MemoryStream();
    await Request.Body.CopyToAsync(ms);
    body = Convert.ToBase64String(ms.ToArray());
    isBase64 = true;
}
else
{
    using var reader = new StreamReader(Request.Body, leaveOpen: true);
    body = await reader.ReadToEndAsync();
    isBase64 = false;
}
// body is now "" for truly empty requests — correct behavior

// Capture IP from forwarded headers (UseForwardedHeaders middleware has already run)
var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
```

`IsBinaryContentType`: returns true for `application/octet-stream`, `image/*`, `audio/*`, `video/*`, `multipart/form-data` (boundary-encoded, treat as binary). Returns false for `text/*`, `application/json`, `application/xml`, `application/x-www-form-urlencoded`.

### 3.9 WebhookController — Notify with Error Isolation

```csharp
// After INSERT — SSE notify is best-effort; never fails the response
try
{
    var summaryDto = MapToSummaryDto(entity);
    await _sseNotifier.NotifyAsync(entity.TokenId, summaryDto);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "SSE notify failed for token {TokenId} — request already persisted", entity.TokenId);
}

return BuildCustomResponse(token.CustomResponse);
```

### 3.10 Token Cache (corrected)

```csharp
// GetOrCreateAsync prevents stampede; does NOT cache null (not-found) results
var cacheKey = $"token:{token}";
var cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
{
    entry.SlidingExpiration = TimeSpan.FromSeconds(60);
    return await _tokenRepository.GetByTokenAsync(token, ct);
});

if (cached is null || !cached.IsActive)
{
    _cache.Remove(cacheKey);    // don't persist null or inactive entries
    return Results.NotFound();
}
```

### 3.11 Startup Pipeline (Program.cs order)

```csharp
// ORDER MATTERS — must be before routing
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost
});

app.UseCors();
app.UseRouting();
app.MapControllers();
app.MapHealthChecks("/health/live",  new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { /* SqlServer check */ });

// Before app.Run() — Kestrel config + EF migration with Polly
builder.WebHost.ConfigureKestrel(opt =>
    opt.Limits.MaxRequestBodySize = opts.MaxRequestSizeMb * 1024L * 1024L);

await Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(5, i => TimeSpan.FromSeconds(i * 2),
        onRetry: (ex, ts) => logger.Warning("DB not ready, retrying in {Delay}s", ts.TotalSeconds))
    .ExecuteAsync(async () =>
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    });
```

### 3.12 RetentionCleanupService (corrected)

```csharp
public sealed class RetentionCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;  // NOT DbContext — would be captive dependency
    private readonly WebhookOptions _options;
    private readonly ILogger<RetentionCleanupService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (_options.RetentionDays <= 0) continue;

                await using var scope = _scopeFactory.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IWebhookRequestRepository>();
                var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);
                var count = await repo.DeleteOlderThanAsync(cutoff, stoppingToken);
                _logger.LogInformation(
                    "Retention cleanup deleted {Count} requests older than {Cutoff:O}", count, cutoff);
            }
            catch (Exception ex)
            {
                // Log and continue — a failed tick is not fatal; retry on next 24h tick
                _logger.LogError(ex, "Retention cleanup failed — will retry on next tick");
            }
        }
    }
}
```

### 3.13 CORS Configuration (corrected)

```csharp
// Program.cs
var rawOrigins = builder.Configuration["Cors:AllowedOrigins"] ?? "";
var origins = rawOrigins.Split(',',
    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (origins.Length > 0)
{
    builder.Services.AddCors(opts => opts.AddDefaultPolicy(policy =>
        policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader()));
}
// If origins is empty: no CORS policy registered → Nginx proxies all frontend traffic
```

---

## 4. URL Generation and Request Flow

### 4.1 Creating a Webhook URL

```
1. User clicks "New Webhook URL" (optional description)
2. POST /api/tokens  { "description": "..." }
3. CreateTokenCommandHandler:
   token.Id     = Guid.NewGuid()   ← internal PK
   token.Token  = Guid.NewGuid()   ← public URL segment (never reused)
   webhookUrl   = $"{WEBHOOK_BASE_URL}/webhook/{token.Token}"
   INSERT WebhookTokens
4. Return 201:
   { id, token, webhookUrl, description, createdAt, isActive: true, customResponse: null }
5. SPA shows URL with Copy button + snackbar confirmation
```

`WEBHOOK_BASE_URL` is required and validated at startup. No runtime fallback.

### 4.2 Receiving a Webhook Request (full path)

```
External caller: DELETE http://localhost/webhook/550e8400-...
  Headers: { Content-Type: application/json, X-Signature: abc123 }
  Body:    { "event": "order.cancelled" }

Step 1 — Nginx
  proxy_set_header Host $host
  proxy_set_header X-Real-IP $remote_addr
  proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for
  proxy_pass http://api:8080

Step 2 — API middleware chain
  UseForwardedHeaders() → resolves real IP into HttpContext.Connection.RemoteIpAddress

Step 3 — WebhookController.CatchAll([FromRoute] Guid token)
  [AcceptVerbs("GET","POST","PUT","DELETE","PATCH","HEAD","OPTIONS")]
  HttpContext.Request.EnableBuffering()   ← first line, always

Step 4 — Read body (always — no ContentLength gate)
  body, isBase64 = ReadBodyAsync(Request)

Step 5 — Size check
  body.Length (bytes) > MaxRequestSizeMb * 1024 * 1024 → 413

Step 6 — Token lookup (GetOrCreateAsync, 60s sliding)
  Cache miss → SELECT WHERE Token=@t AND IsActive=1
  Not found  → remove from cache, return 404

Step 7 — Capture
  requestId = Guid.NewGuid()
  ip = HttpContext.Connection.RemoteIpAddress.ToString()
  snapshot = { id, tokenId, method, path, queryString, headers (JSON), body, isBase64,
               contentType, ipAddress=ip, userAgent, sizeBytes=body.Length, receivedAt=UtcNow }

Step 8 — INSERT to WebhookDb (~2–5ms)

Step 9 — SSE notify (best-effort, error isolated)
  try { await sseNotifier.NotifyAsync(tokenId, summaryDto) }
  catch { log warning }

Step 10 — Return response
  CustomResponse set → status + custom headers + body
  Not set            → 200 OK, empty body
  Total: ~5–10ms
```

---

## 5. Solution Structure

```
WebhookService.sln
│
├── src/
│   │
│   ├── WebhookService.Domain/
│   │   ├── Entities/
│   │   │   ├── WebhookToken.cs
│   │   │   └── WebhookRequest.cs
│   │   ├── ValueObjects/
│   │   │   └── CustomResponse.cs
│   │   ├── Repositories/
│   │   │   ├── IWebhookTokenRepository.cs
│   │   │   └── IWebhookRequestRepository.cs   ← includes DeleteOlderThanAsync
│   │   └── Services/
│   │       ├── ISseNotifier.cs
│   │       └── SseEvent.cs                    ← record SseEvent(string EventName, string Data)
│   │
│   ├── WebhookService.Application/
│   │   ├── Tokens/
│   │   │   ├── Commands/
│   │   │   │   ├── CreateToken/
│   │   │   │   ├── DeleteToken/               ← soft-delete + hard-delete requests + cache + NotifyTokenDeleted
│   │   │   │   ├── SetCustomResponse/         ← UPDATE + cache invalidate
│   │   │   │   └── ResetCustomResponse/       ← clear + cache invalidate
│   │   │   └── Queries/
│   │   │       ├── GetTokens/                 ← WHERE IsActive=1 only
│   │   │       └── GetTokenById/              ← 404 if inactive
│   │   ├── Requests/
│   │   │   ├── Commands/
│   │   │   │   ├── DeleteRequest/
│   │   │   │   └── ClearRequests/
│   │   │   └── Queries/
│   │   │       ├── GetRequests/               ← PagedResult<SummaryDto>; pageSize≤100; LIKE search
│   │   │       ├── GetRequestById/            ← DetailDto (with body)
│   │   │       └── ExportRequest/             ← JSON bytes; Content-Disposition header
│   │   ├── Common/
│   │   │   ├── Behaviors/
│   │   │   │   ├── LoggingBehavior.cs         ← name + duration only; no payload
│   │   │   │   └── ValidationBehavior.cs
│   │   │   ├── DTOs/
│   │   │   │   ├── WebhookTokenDto.cs
│   │   │   │   ├── WebhookRequestSummaryDto.cs
│   │   │   │   └── WebhookRequestDetailDto.cs
│   │   │   └── Models/
│   │   │       └── PagedResult.cs             ← { Items, Page, PageSize, Total }
│   │   └── DependencyInjection.cs
│   │
│   ├── WebhookService.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── ApplicationDbContext.cs
│   │   │   ├── Configurations/
│   │   │   │   ├── WebhookTokenConfiguration.cs   ← owned entity, column lengths
│   │   │   │   └── WebhookRequestConfiguration.cs ← Path max 2048, QueryString max 4096
│   │   │   ├── Repositories/
│   │   │   │   ├── WebhookTokenRepository.cs
│   │   │   │   └── WebhookRequestRepository.cs    ← includes DeleteOlderThanAsync
│   │   │   └── Migrations/
│   │   ├── BackgroundServices/
│   │   │   └── RetentionCleanupService.cs     ← IServiceScopeFactory; try/catch; PeriodicTimer(24h)
│   │   ├── Sse/
│   │   │   ├── NullSseNotifier.cs             ← no-op; used in Phase 1–2; replaced in Phase 3
│   │   │   └── SseNotifier.cs                 ← ConcurrentDictionary; atomic count; bounded channels
│   │   └── DependencyInjection.cs             ← registers NullSseNotifier initially; swap in Phase 3
│   │
│   └── WebhookService.API/
│       ├── Controllers/
│       │   ├── TokensController.cs
│       │   ├── RequestsController.cs
│       │   ├── WebhookController.cs            ← EnableBuffering; always read body; notify with try/catch
│       │   └── EventsController.cs             ← atomic connection check; SSE loop; ping timer
│       ├── Middleware/
│       │   └── GlobalExceptionMiddleware.cs    ← ProblemDetails; maps domain exceptions to HTTP codes
│       ├── Options/
│       │   ├── WebhookOptions.cs
│       │   └── WebhookOptionsValidator.cs      ← IValidateOptions; ValidateOnStart
│       └── Program.cs                          ← ForwardedHeaders; Kestrel limit; CORS; MigrateAsync+Polly
│
├── tests/
│   ├── WebhookService.UnitTests/
│   ├── WebhookService.IntegrationTests/
│   └── WebhookService.E2ETests/
│
├── frontend/
│   └── webhook-spa/
│       └── src/app/
│           ├── core/
│           │   ├── models/
│           │   │   ├── token.model.ts
│           │   │   ├── request-summary.model.ts
│           │   │   └── request-detail.model.ts
│           │   ├── services/
│           │   │   ├── token.service.ts
│           │   │   ├── request.service.ts
│           │   │   └── sse.service.ts          ← EventSource → Observable; 3s reconnect; connected$ BehaviorSubject
│           │   └── interceptors/
│           │       └── http-error.interceptor.ts  ← global HTTP error toasts
│           ├── features/
│           │   ├── dashboard/                  ← token list + create button
│           │   ├── token-detail/               ← split view: request list (SSE-live) + detail panel
│           │   └── custom-response/            ← dialog (status, content-type, headers k/v, body)
│           └── shared/
│               ├── copy-button.component.ts
│               ├── search-bar.component.ts     ← 300ms debounce; resets page to 1 on change
│               ├── body-viewer.component.ts    ← JSON pretty-print; Base64 decode if isBodyBase64
│               └── sse-status.component.ts     ← green dot (connected) / red dot (disconnected)
│
├── docker/
│   ├── api/Dockerfile                          ← multi-stage: SDK → ASP.NET 10 runtime
│   ├── frontend/
│   │   ├── Dockerfile                          ← multi-stage: Node → Nginx Alpine
│   │   └── nginx.conf                          ← Host + IP headers; SSE settings; no /internal/ block
│   └── sqlserver/
│       ├── Dockerfile
│       ├── entrypoint.sh
│       └── init.sql                            ← CREATE DATABASE WebhookDb only
│
├── docker-compose.yml
├── docker-compose.override.yml
└── .env.example
```

**4 src projects. No Worker. No Dashboard.**

---

## 6. Docker Compose

### Services

| Service | Image | Ports | Purpose |
|---------|-------|-------|---------|
| `sqlserver` | custom build | 1433 | WebhookDb |
| `seq` | datalust/seq:latest | 5341 (ingest), 5342 (UI) | Structured logs |
| `api` | custom build | 8080 | API + BackgroundService |
| `frontend` | custom build (Nginx) | 80 | Angular SPA |

### MSSQL Custom Image

`docker/sqlserver/Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/mssql/server:2022-latest
COPY init.sql /init.sql
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
```

`docker/sqlserver/entrypoint.sh`:
```bash
#!/bin/bash
/opt/mssql/bin/sqlservr &
MSSQL_PID=$!

echo "Waiting for SQL Server to be ready..."
until /opt/mssql-tools18/bin/sqlcmd \
      -S localhost -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" > /dev/null 2>&1; do
    sleep 2
done

echo "Running init.sql..."
/opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C -i /init.sql

echo "Initialisation complete."
wait $MSSQL_PID
```

`docker/sqlserver/init.sql`:
```sql
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'WebhookDb')
    CREATE DATABASE WebhookDb;
GO
```

### docker-compose.yml

```yaml
name: webhook-service

services:

  sqlserver:
    build:
      context: docker/sqlserver
      dockerfile: Dockerfile
    environment:
      SA_PASSWORD: "${SA_PASSWORD}"
      ACCEPT_EULA: "Y"
      MSSQL_PID: "Developer"
    ports: ["1433:1433"]
    volumes:
      - sqlserver-data:/var/opt/mssql
    healthcheck:
      test: ["/opt/mssql-tools18/bin/sqlcmd",
             "-S", "localhost", "-U", "sa", "-P", "${SA_PASSWORD}", "-C",
             "-Q", "SELECT name FROM sys.databases WHERE name = 'WebhookDb'"]
      interval: 10s
      retries: 12
      start_period: 45s
    networks: [webhook-net]

  seq:
    image: datalust/seq:latest
    environment: { ACCEPT_EULA: "Y" }
    ports: ["5341:5341", "5342:80"]
    volumes: [seq-data:/data]
    networks: [webhook-net]

  api:
    build: { context: ., dockerfile: docker/api/Dockerfile }
    ports: ["8080:8080"]
    environment:
      ASPNETCORE_HTTP_PORTS:            "8080"           # explicit — do not rely on image defaults
      ConnectionStrings__WebhookDb:    "Server=sqlserver;Database=WebhookDb;User Id=sa;Password=${SA_PASSWORD};TrustServerCertificate=True"
      Seq__ServerUrl:                  "http://seq:5341"
      Webhook__BaseUrl:                "${WEBHOOK_BASE_URL}"       # required — no default
      Webhook__RetentionDays:          "${RETENTION_DAYS:-7}"
      Webhook__MaxRequestSizeMb:       "${MAX_REQUEST_SIZE_MB:-5}"
      Cors__AllowedOrigins:            ""                # empty = no CORS (Nginx proxies all)
    depends_on:
      sqlserver: { condition: service_healthy }
      seq:       { condition: service_started }
    networks: [webhook-net]

  frontend:
    build: { context: ., dockerfile: docker/frontend/Dockerfile }
    ports: ["80:80"]
    depends_on: [api]
    networks: [webhook-net]

volumes:
  sqlserver-data:
  seq-data:

networks:
  webhook-net:
    driver: bridge
```

### docker-compose.override.yml (local dev)

```yaml
services:
  api:
    environment:
      Cors__AllowedOrigins:       "http://localhost:4200"
      ASPNETCORE_ENVIRONMENT:     Development
      Webhook__BaseUrl:           "http://localhost"    # override for local dev
  sqlserver:
    ports: ["1433:1433"]
```

### .env.example

```
SA_PASSWORD=YourStr0ngP@ssword!
WEBHOOK_BASE_URL=http://your-server-hostname-or-ip
RETENTION_DAYS=7
MAX_REQUEST_SIZE_MB=5
```

**`WEBHOOK_BASE_URL` has no default — must be set in `.env`.** Application startup validation will fail if absent.

### Nginx config (`docker/frontend/nginx.conf`)

```nginx
server {
    listen 80;
    client_max_body_size 0;          # body size enforcement is in API/Kestrel

    # Forward real client IP to API — required for correct IP capture
    proxy_set_header Host              $host;
    proxy_set_header X-Real-IP         $remote_addr;
    proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;

    location /api/events/ {          # SSE — must disable all buffering
        proxy_pass                    http://api:8080;
        proxy_http_version            1.1;
        proxy_set_header              Connection "";
        proxy_buffering               off;
        proxy_cache                   off;
        chunked_transfer_encoding     on;
        proxy_read_timeout            3600s;   # keep SSE open up to 1h
    }

    location /api/ {
        proxy_pass         http://api:8080;
        proxy_http_version 1.1;
    }

    location /webhook/ {
        proxy_pass         http://api:8080;
        proxy_http_version 1.1;
    }

    location /health {
        proxy_pass http://api:8080;
    }

    location / {
        root       /usr/share/nginx/html;
        index      index.html;
        try_files  $uri $uri/ /index.html;
    }
}
```

Note: No `/internal/` block — that endpoint no longer exists.

---

## 7. Technology Stack

### Backend — .NET 10

| Package | Purpose |
|---------|---------|
| MediatR | CQRS dispatching |
| FluentValidation.AspNetCore | Input validation (pageSize≤100, etc.) |
| Microsoft.EntityFrameworkCore.SqlServer | ORM + migrations |
| Serilog.AspNetCore + Serilog.Sinks.Seq | Structured logging |
| Microsoft.Extensions.Diagnostics.HealthChecks.SqlServer | `/health/ready` check |
| Polly | Startup migration retry |
| Swashbuckle.AspNetCore | OpenAPI / Swagger |
| Microsoft.AspNetCore.HttpOverrides | `UseForwardedHeaders()` for real IP |

No Hangfire. No Windows Service packages.

### Testing

| Package | Purpose |
|---------|---------|
| xUnit | Test framework |
| NSubstitute | Mocking |
| FluentAssertions | Readable assertions |
| Microsoft.AspNetCore.Mvc.Testing | `WebApplicationFactory` |
| Testcontainers.MsSql | Real MSSQL container for integration tests |
| Microsoft.Playwright | E2E browser automation |

### Frontend — Angular latest

| Package | Purpose |
|---------|---------|
| Angular standalone components | Framework |
| Angular Material | UI + responsive layout |
| RxJS | EventSource wrapped as Observable; `BehaviorSubject` for SSE state |

---

## 8. Implementation Phases

> Unit tests written **alongside** each backend phase. Phases 9–11 are integration, E2E, and gap-fill.

---

### Phase 1 — Solution Scaffold & Domain
**Status: Complete | Target: Days 1–2**

**Local dev DB (before Docker in Phase 7):**
```powershell
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=Dev@123456!" `
    -p 1433:1433 --name webhook-sql `
    mcr.microsoft.com/mssql/server:2022-latest
```

- [ ] `dotnet new sln WebhookService`
- [ ] 4 src projects: Domain, Application, Infrastructure, API
- [ ] 3 test projects: UnitTests, IntegrationTests, E2ETests
- [ ] Project references: API → Application + Infrastructure; Infrastructure → Application → Domain
- [ ] Domain entities: `WebhookToken`, `WebhookRequest` (with `IsBodyBase64`), `CustomResponse`
- [ ] `SseEvent` record in `Domain/Services/`
- [ ] `ISseNotifier` interface (with `SubscribeAsync`, `NotifyAsync`, `NotifyTokenDeleted`)
- [ ] `NullSseNotifier` (no-op) in `Infrastructure/Sse/` — registered in DI as `ISseNotifier`
- [ ] `IWebhookTokenRepository` + `IWebhookRequestRepository` (with `DeleteOlderThanAsync`)
- [ ] `ApplicationDbContext` with `WebhookToken` + `WebhookRequest` DbSets
- [ ] EF entity configurations: column lengths, indexes, owned `CustomResponse`, nullable handling
- [ ] Initial EF migration (`Add-Migration InitialCreate`)
- [ ] `WebhookOptions` with `IValidateOptions<WebhookOptions>`, `.ValidateOnStart()`
- [ ] `dotnet build` green; migration generates correct schema

Unit tests: `WebhookTokenTests.cs` (entity invariants, `CustomResponse` value object).

Exit criteria: Build green; `dotnet ef migrations script` produces valid SQL with `IsBodyBase64` column and correct max lengths.

---

### Phase 2 — API: Token CRUD + Webhook Receiver
**Status: Complete | Target: Days 3–5**

- [ ] MediatR + FluentValidation wired; `LoggingBehavior` + `ValidationBehavior`
- [ ] `CreateTokenCommand` + handler (UUID generation; `webhookUrl` from `WEBHOOK_BASE_URL`)
- [ ] `DeleteTokenCommand` + handler (soft-delete + hard-delete requests + cache invalidate + `NotifyTokenDeleted`)
- [ ] `SetCustomResponseCommand` + handler (**cache invalidate**)
- [ ] `ResetCustomResponseCommand` + handler (**cache invalidate**)
- [ ] `GetTokensQuery` — filters `WHERE IsActive = 1`
- [ ] `GetTokenByIdQuery` — 404 if inactive
- [ ] `TokensController` — full CRUD
- [ ] `WebhookController`:
  - [ ] `Request.EnableBuffering()` as first line
  - [ ] Read body unconditionally (no ContentLength gate)
  - [ ] Size check on body bytes
  - [ ] `GetOrCreateAsync` token lookup (60s sliding); guard against caching null
  - [ ] Binary detection + Base64 encoding
  - [ ] Real IP from `HttpContext.Connection.RemoteIpAddress` (UseForwardedHeaders runs before)
  - [ ] Synchronous INSERT
  - [ ] `NotifyAsync` in try/catch (uses `NullSseNotifier` in this phase — no-op)
  - [ ] Return custom/default response
- [ ] `UseForwardedHeaders()` configured in `Program.cs` (before routing)
- [ ] Kestrel `MaxRequestBodySize` from `WebhookOptions` in `Program.cs`
- [ ] `[RequestSizeLimit]` attribute on WebhookController action
- [ ] CORS from `Cors:AllowedOrigins` with `RemoveEmptyEntries` split
- [ ] `GlobalExceptionMiddleware` → ProblemDetails
- [ ] Swagger configured
- [ ] EF migrations applied via Polly retry on startup

Unit tests: `CreateTokenHandlerTests`, `DeleteTokenHandlerTests`, `SetCustomResponseHandlerTests` (verify cache invalidated), `ResetCustomResponseHandlerTests` (verify cache invalidated).

Exit criteria: `curl -X POST http://localhost:8080/webhook/{guid}` returns 200; row in DB with correct IpAddress (not 127.0.0.1); cache invalidation verified via unit test.

---

### Phase 3 — SSE
**Status: Complete | Target: Days 6–7**

- [ ] `SseNotifier` in `Infrastructure/Sse/`:
  - [ ] `ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<SseEvent>>>`
  - [ ] Atomic connection counting with per-tokenId lock on add — check + add in one critical section
  - [ ] `SubscribeAsync`: check count (throw `TooManyConnectionsException` if ≥ 10); create bounded channel (capacity=50); add; yield; cleanup on cancel
  - [ ] `NotifyAsync`: `TryWrite` to all channels (non-blocking; silently drops if full)
  - [ ] `NotifyTokenDeleted`: write event; `Channel.Writer.Complete()` all; remove from dict
- [ ] Replace `NullSseNotifier` registration with real `SseNotifier` in `Infrastructure/DependencyInjection.cs`
- [ ] `EventsController`:
  - [ ] Catch `TooManyConnectionsException` → 429
  - [ ] `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`
  - [ ] `retry: 3000` field in SSE response (controls browser reconnect interval)
  - [ ] Loop: `await foreach (var evt in sseNotifier.SubscribeAsync(tokenId, ct))`
  - [ ] Background timer sends `comment: ping` every 15s
- [ ] `TooManyConnectionsException` in Domain (mapped to 429 in GlobalExceptionMiddleware)
- [ ] Verified with `curl --no-buffer http://localhost:8080/api/events/{uuid}`

Unit tests: `SseNotifierTests` (subscribe, notify, token-deleted, 11th connection throws).

Exit criteria: Events appear in SSE stream immediately after `curl -X POST /webhook/{uuid}`; `token-deleted` closes stream; 11th connection → 429.

---

### Phase 4 — Request Management API
**Status: Complete | Target: Days 8–9**

- [ ] `GetRequestsQuery` + handler — `PagedResult<WebhookRequestSummaryDto>` (no body); LIKE search on Headers+Body; `pageSize` ≤ 100 enforced by FluentValidation; two DB calls (COUNT + paged SELECT)
- [ ] `GetRequestByIdQuery` + handler — `WebhookRequestDetailDto` (with body + `IsBodyBase64`)
- [ ] `ExportRequestQuery` + handler — serialises `WebhookRequestDetailDto` to JSON bytes
- [ ] `DeleteRequestCommand`, `ClearRequestsCommand` + handlers
- [ ] `RequestsController` — all endpoints
- [ ] `PagedResult<T>` model in Application/Common/Models

Unit tests: `GetRequestsHandlerTests` (pageSize limit, LIKE search, no body in SummaryDto), `ExportRequestHandlerTests`.

Exit criteria: List has no body; detail has body; `pageSize=101` → 400; search term filters correctly; export downloads valid JSON.

---

### Phase 5 — Retention Cleanup BackgroundService
**Status: Complete | Target: Day 10**

- [ ] `RetentionCleanupService` in `Infrastructure/BackgroundServices/`:
  - [ ] `IServiceScopeFactory` injection (NOT `DbContext` — captive dependency)
  - [ ] `PeriodicTimer(TimeSpan.FromHours(24))`
  - [ ] `try/catch` wraps entire cleanup body — errors logged, service continues
  - [ ] `RetentionDays <= 0` → skip (keep forever)
  - [ ] Creates `IAsyncScope` per tick; resolves `IWebhookRequestRepository`
  - [ ] Structured log: `{Count}` records deleted
- [ ] `IWebhookRequestRepository.DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct)` + implementation
- [ ] Registered as `services.AddHostedService<RetentionCleanupService>()`

Unit tests: `RetentionCleanupServiceTests` — `RetentionDays=0` skips delete; `RetentionDays=7` calls `DeleteOlderThanAsync` with correct cutoff; DB exception is caught and does not propagate.

Exit criteria: Unit tests pass; manual verification by temporarily setting timer to 10s interval and `RETENTION_DAYS=0.001`.

---

### Phase 6 — Observability
**Status: Complete | Target: Day 11**

- [ ] Serilog + SEQ sink in API — structured JSON to console + SEQ
- [ ] Structured log events (no sensitive payload logged):
  - [ ] Webhook received: `{TokenId, Method, SizeBytes, DurationMs, IpAddress}`
  - [ ] SSE connected/disconnected: `{TokenId, ConnectionCount}`
  - [ ] Retention: `{DeletedCount, RetentionDays}`
  - [ ] Token cache hit/miss: `{TokenId, CacheHit}` (debug level)
- [ ] Health checks:
  - [ ] `/health/live` — `Predicate = _ => false` (always 200, no DB call)
  - [ ] `/health/ready` — `SqlServerHealthCheck` on `WebhookDb` connection string
- [ ] SEQ accessible at `http://localhost:5342` with searchable structured logs
- [ ] `LoggingBehavior` emits duration log for each MediatR handler

Exit criteria: Send 5 webhook requests; each appears in SEQ with `{TokenId, Method, SizeBytes}`; `/health/ready` returns 200.

---

### Phase 7 — Docker Setup
**Status: Complete | Target: Days 12–13**

> Do this before SPA so the Angular dev server runs against a containerised backend.

- [ ] Custom MSSQL Dockerfile + `entrypoint.sh` + `init.sql`
- [ ] `docker/api/Dockerfile`:
  ```dockerfile
  FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
  WORKDIR /src
  COPY . .
  RUN dotnet publish src/WebhookService.API -c Release -o /app/publish

  FROM mcr.microsoft.com/dotnet/aspnet:10.0
  WORKDIR /app
  COPY --from=build /app/publish .
  ENTRYPOINT ["dotnet", "WebhookService.API.dll"]
  ```
- [ ] `docker/frontend/Dockerfile` (Node build → Nginx Alpine copy)
- [ ] `docker/frontend/nginx.conf` — Host header, X-Forwarded-For on all locations; SSE settings; no `/internal/` block
- [ ] `docker-compose.yml` — 4 services; `ASPNETCORE_HTTP_PORTS: "8080"`; no default for `Webhook__BaseUrl`
- [ ] `docker-compose.override.yml` — CORS for Angular dev; `Webhook__BaseUrl: http://localhost`
- [ ] `.env.example` — document that `WEBHOOK_BASE_URL` is required
- [ ] `docker compose up --build` — all 4 containers healthy
- [ ] Verify: `curl http://localhost/webhook/{guid}` → 200; SSE stream delivers events; SEQ receives logs; `/health/ready` → 200; captured `IpAddress` is NOT `172.x.x.x`

Exit criteria: All 4 services healthy; IP capture shows real host IP; webhook URL in response is `http://localhost/webhook/...` (not `http://api:8080/...`).

---

### Phase 8 — Angular SPA
**Status: Complete | Target: Days 14–18**

- [ ] `ng new webhook-spa --standalone --routing --style=css`
- [ ] Angular Material + theming
- [ ] `environments/environment.ts` + `environment.prod.ts` (API base URL)
- [ ] Models: `WebhookTokenDto`, `WebhookRequestSummaryDto`, `WebhookRequestDetailDto`
- [ ] `HttpErrorInterceptor` — global toast on API errors
- [ ] `TokenService`, `RequestService`
- [ ] `SseService`:
  - [ ] `EventSource` → `Observable<SseEvent>` via RxJS
  - [ ] `error` event → close and reconnect after 3s
  - [ ] `connected$: BehaviorSubject<boolean>`
  - [ ] Emits typed events: `new-request`, `token-deleted`
- [ ] `DashboardComponent` — token list; "New URL" button; copy URL
- [ ] `TokenDetailComponent`:
  - [ ] Left panel: request list (SSE live-prepend); `SseStatusComponent`
  - [ ] `token-deleted` event → `router.navigate(['/'])`
  - [ ] Right panel: request detail on row click
- [ ] `CustomResponseDialogComponent` — status code, content-type, headers (key-value list), body
- [ ] `SearchBarComponent` — 300ms debounce; emitting **resets page to 1**
- [ ] `CopyButtonComponent` — `navigator.clipboard.writeText` + Angular Material Snackbar
- [ ] `BodyViewerComponent` — JSON pretty-print; `atob()` decode if `isBodyBase64`; raw text fallback
- [ ] `SseStatusComponent` — green dot (`connected$=true`) / red dot
- [ ] "Export JSON" — `<a [href]="blobUrl" download="request-{id}.json">` Blob trick
- [ ] Responsive (Angular Material flex layout / breakpoints)
- [ ] Build: `ng build` with `outputPath` pointing to location copied by frontend Dockerfile

UI layout:
```
┌────────────────────────────────────────────────────────────────────────┐
│ [← Back]   550e8400...   [● LIVE]  [Copy URL]  [Configure Response]    │
├──────────────────────────┬─────────────────────────────────────────────┤
│  [search............]    │  Method: DELETE    Received: 10:45:02        │
│  ─────────────────────── │  IP: 203.0.113.42  Size: 342 bytes          │
│  DELETE  10:45:02  ◄──── │                                              │
│  GET     10:44:55        │  Headers                                     │
│  POST    10:44:30        │  Content-Type: application/json              │
│                          │  X-Signature: sha256=abc123                  │
│                          │                                              │
│                          │  Body                                        │
│                          │  {                                           │
│                          │    "event": "order.cancelled"                │
│                          │  }                                           │
│                          │                                              │
│                          │  [Export JSON]   [Delete]                    │
└──────────────────────────┴─────────────────────────────────────────────┘
```

Exit criteria: Create URL → send request → appears live → SSE green → real sender IP shown → inspect body → export → delete token → SPA navigates back.

---

### Phase 9 — Unit Tests (gap-fill)
**Status: Complete | Target: Days 19–20**

Unit tests written alongside Phases 1–6. This phase fills gaps and verifies ≥ 80% Application layer coverage.

```
WebhookService.UnitTests/
├── Domain/
│   └── WebhookTokenTests.cs
└── Application/
    ├── CreateTokenHandlerTests.cs
    ├── DeleteTokenHandlerTests.cs
    ├── SetCustomResponseHandlerTests.cs       ← cache invalidated
    ├── ResetCustomResponseHandlerTests.cs     ← cache invalidated
    ├── GetRequestsHandlerTests.cs             ← pageSize cap, LIKE search, no body in result
    ├── RetentionCleanupServiceTests.cs        ← RetentionDays=0 skips; exception does not propagate
    └── SseNotifierTests.cs                   ← 11th connection throws; notify drops if full; token-deleted completes
```

---

### Phase 10 — Integration Tests
**Status: Complete | Target: Days 21–22**

`WebApplicationFactory<Program>` + Testcontainers.MsSql (real SQL Server container).

```
WebhookService.IntegrationTests/
├── TokensApiTests.cs         ← CRUD; 404 on soft-deleted; cache invalidated on CustomResponse update
├── WebhookReceiverTests.cs   ← row in DB after POST; body captured for chunked requests (no Content-Length)
├── RequestsApiTests.cs       ← list has no body; detail has body; pageSize=101 → 400; search filters
├── SseConnectionTests.cs     ← SSE delivers event immediately; 11th connection → 429
│                                (use CancellationTokenSource with short timeout per connection)
└── HealthCheckTests.cs       ← /health/live always 200; /health/ready 200 with DB up
```

---

### Phase 11 — E2E Tests (Playwright)
**Status: Complete | Target: Days 23–24**

**Prerequisite:** Docker Compose v2 (`docker compose version` must show v2.x — the `--wait` flag is not available in v1).

`GlobalSetup` runs `docker compose up -d --wait`; `GlobalTeardown` runs `docker compose down -v`.

```
WebhookService.E2ETests/
├── Fixtures/
│   └── DockerComposeFixture.cs    ← GlobalSetup/Teardown; waits for health checks
├── CreateTokenTest.cs             ← create, copy URL (snackbar), URL contains correct BaseUrl
├── ReceiveRequestRealtimeTest.cs  ← POST via HttpClient; appears in SPA list without refresh
├── SseIndicatorTest.cs            ← green dot visible when SSE connected
├── SearchRequestsTest.cs          ← 3 requests; search term filters to correct one; page resets to 1
├── CustomResponseTest.cs          ← configure 201; send request; caller receives 201
├── ExportRequestTest.cs           ← download JSON; verify content matches request
└── DeleteTokenTest.cs             ← delete; SPA navigates to dashboard; token absent from list
```

---

## 9. Testing Strategy

| Layer | Tools | Scope | Infrastructure |
|-------|-------|-------|----------------|
| Unit | xUnit + NSubstitute + FluentAssertions | Domain + Application + Infrastructure services | All mocked |
| Integration | xUnit + WebApplicationFactory + Testcontainers.MsSql | All API endpoints; real DB | SQL Server container |
| E2E | Playwright .NET + DockerComposeFixture | 7 critical user journeys | Full stack (4 containers) |

Coverage target: ≥ 80% Application layer (enforced by coverlet in CI). Integration covers all API contracts including chunked body capture and SSE limits. E2E covers the core user flows end-to-end.

---

## 10. Known Limitations

| Limitation | Impact | Acceptable Because |
|------------|--------|--------------------|
| LIKE search on NVARCHAR(MAX) — full scan within a token's rows | Slow for tokens with 10k+ requests | < 100 URLs, small team; FTS upgrade documented |
| SSE notify is best-effort — channel full → event dropped | User may miss live event; refresh shows it | Request is durable in DB |
| Single API instance — SSE channels are in-memory | Horizontal scaling breaks SSE | Redis upgrade documented |
| Binary payloads Base64-encoded — ~33% storage overhead | Larger DB rows | Max 5–10MB per request, small scale |
| `PeriodicTimer(24h)` resets on restart | Cleanup may run slightly late | Daily timing is not critical |
| Soft-deleted tokens remain in DB | DB accumulates inactive rows over time | Negligible at < 100 URL scale; can be purged manually |
| `RetentionCleanupService` first tick is 24h after startup | No cleanup on day of startup | Acceptable |
| No bulk export (all requests for a token as one file) | Out of scope | Documented as future path |

---

## 11. Future-Proofing Notes

| Topic | Trigger | Migration Path |
|-------|---------|----------------|
| Redis for SSE + token cache | API needs horizontal scaling | Replace `SseNotifier` with Redis Pub/Sub subscriber. Replace `IMemoryCache` with `IDistributedCache`. `ISseNotifier` interface unchanged; implementation replaced. |
| Authentication | Phase 2 product requirement | JWT middleware + `[Authorize]` on all controllers. Domain/Application unchanged. |
| Full-Text Search | LIKE too slow | Add FTS catalog in EF migration; replace `LIKE` with `EF.Functions.Contains()` in `GetRequestsQueryHandler`. One handler change. |
| Bulk export (all requests for a token) | User request | `GET /api/tokens/{uuid}/requests/export` returning NDJSON or ZIP. |
| Hangfire (if complex jobs needed) | Retry logic, job scheduling requirements grow | Add `WebhookService.Worker` project; introduce `HangfireDb`; move `RetentionCleanupService` to recurring Hangfire job. |

---

## 12. Access Points

| Service | URL |
|---------|-----|
| Angular SPA | http://localhost |
| API Swagger | http://localhost:8080/swagger |
| SEQ Logs | http://localhost:5342 |
| API Health (liveness) | http://localhost:8080/health/live |
| API Health (readiness) | http://localhost:8080/health/ready |
| SQL Server (local tools) | localhost:1433 (via docker-compose.override.yml) |

---

*Updated at the end of each completed phase.*
