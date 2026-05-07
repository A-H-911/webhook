# Webhook Service

A self-hosted webhook inspection and debugging tool. Generate unique URLs, send HTTP requests to them from any source, and inspect every captured request in real time — similar to webhook.site but running entirely on your own infrastructure.

---

## Table of Contents

1. [Navigation Guide](#1-navigation-guide)
2. [System Requirements](#2-system-requirements)
3. [Features](#3-features)
4. [Quick Start](#4-quick-start)
5. [Architecture Overview (HLD)](#5-architecture-overview-hld)
6. [Data Flows](#6-data-flows)
7. [Low-Level Design (LLD)](#7-low-level-design-lld)
   - [7.1 Domain Entities](#71-domain-entities)
   - [7.2 DTOs](#72-dtos)
   - [7.3 API Contract](#73-api-contract)
   - [7.4 SSE Architecture](#74-sse-architecture)
   - [7.5 Application Layer — CQRS Map](#75-application-layer--cqrs-map)
   - [7.6 WebhookOptions & Startup Validation](#76-webhookoptions--startup-validation)
   - [7.7 Token Cache Strategy](#77-token-cache-strategy)
8. [Solution Structure](#8-solution-structure)
9. [Docker Compose](#9-docker-compose)
10. [Configuration Reference](#10-configuration-reference)
11. [Security Model](#11-security-model)
12. [Development Guide](#12-development-guide)
13. [Testing](#13-testing)
14. [Observability](#14-observability)
15. [Contributing](#15-contributing)
16. [Design Decisions](#16-design-decisions)
17. [Trade-offs & Known Limitations](#17-trade-offs--known-limitations)
18. [Future Roadmap](#18-future-roadmap)
19. [Access Points](#19-access-points)
20. [Troubleshooting](#20-troubleshooting)

---

## 1. Navigation Guide

Find what you need based on your role:

| Role | Go to |
|------|-------|
| **Operator** — deploy and configure the stack | §4 Quick Start, §9 Docker Compose, §10 Configuration, §11 Security, §20 Troubleshooting |
| **End User** — use the UI to inspect webhooks | §4 Quick Start, §6 Data Flows (Flows A–B), §7.3 API Contract |
| **Developer** — build features or extend the service | §7 LLD, §8 Structure, §12 Development Guide, §13 Testing, §15 Contributing |
| **Architect / Reviewer** — evaluate design | §5 HLD, §6 Data Flows, §7 LLD, §16 Design Decisions, §17 Trade-offs |

---

## 2. System Requirements

| Requirement | Minimum |
|-------------|---------|
| Docker Engine | 24+ with Compose v2 (`docker compose version` must show v2.x) |
| RAM | 3 GB available (SQL Server 2022 requires 2 GB alone) |
| Disk | 5 GB free (SQL Server image ~1.5 GB; volumes grow with data) |
| OS | Linux, macOS, or Windows with Docker Desktop |
| Outbound internet | Required on first run to pull `mcr.microsoft.com/mssql/server:2022-latest` and `datalust/seq:latest` |

For local development without Docker:

| Requirement | Version |
|-------------|---------|
| .NET SDK | 10.0+ |
| Node.js | 20+ |
| Docker | For integration tests (Testcontainers pulls SQL Server automatically) |

---

## 3. Features

- **Unique webhook URLs** — create as many token URLs as you need; each is independent
- **Real-time inspection** — live Server-Sent Events push new requests to the UI instantly (no polling)
- **Full request capture** — method, path, query string, headers, body, IP address, user agent, size, timestamp
- **Binary payload support** — binary bodies are Base64-encoded and decoded transparently in the viewer
- **Custom responses** — configure the status code, content type, headers, and body each token returns to callers
- **Search and pagination** — filter captured requests by headers or body content; max 100 per page
- **Export** — download any individual request as a JSON file
- **Retention cleanup** — automatically delete requests older than a configurable number of days (BackgroundService, 24-hour cycle)
- **Structured logging** — all events streamed to SEQ for querying and alerting
- **SSE connection safety** — max 10 concurrent connections per token; 11th connection receives HTTP 429
- **Dark mode** — defaults to dark; user-toggleable, persisted in `localStorage`, flash-of-wrong-theme safe via inline script
- **Cookie-based authentication** — single admin credential configured via env vars; BCrypt-hashed password; session cookie (`SameSite=Lax`, `HttpOnly`); see §11 Security Model

---

## 4. Quick Start

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) or Docker Engine + Compose v2

### 1. Clone and configure

```bash
git clone <repo-url>
cd webhook
cp .env.example .env
```

Edit `.env`:

```env
SA_PASSWORD=YourStr0ngP@ssword!
WEBHOOK_BASE_URL=http://your-server-hostname-or-ip:8088
RETENTION_DAYS=7
MAX_REQUEST_SIZE_MB=5
```

> `WEBHOOK_BASE_URL` is the address that **external callers** can reach — not the internal container name. For a LAN deployment use `http://192.168.1.10:8088`. The application refuses to start if this value is absent or empty.

### 2. Start the stack

```bash
docker compose up -d
```

SQL Server takes up to 45 s to initialize on first boot. The API automatically retries EF migrations with exponential backoff — you do not need to wait manually.

### 3. Open the UI

Navigate to **http://localhost:8088**

### 4. Create a webhook URL and send a request

Click **New URL** in the dashboard. A unique URL like `http://your-server:8088/webhook/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` is generated. Send any HTTP request to it:

```bash
curl -X POST "http://localhost:8088/webhook/<token-guid>" \
  -H "Content-Type: application/json" \
  -d '{"event": "order.created", "orderId": 42}'
```

The request appears in the dashboard in real time via SSE.

### Shut down

```bash
docker compose down        # keeps data volumes
docker compose down -v     # wipes all data
```

---

## 5. Architecture Overview (HLD)

### 5.1 System Context

```
External callers ──────────────────► ANY /webhook/{uuid}
  (real IP forwarded via X-Forwarded-For)         │
                               ┌──────────────────▼─────────────────────────┐
                               │    WebhookService.API  (:8080)              │
                               │  UseForwardedHeaders() → real IP captured   │
                               │  Token CRUD · SSE stream · Webhook receiver │
                               │  INSERT to DB → ISseNotifier.NotifyAsync()  │
                               │  RetentionCleanupService (BackgroundService)│
                               └──────┬─────────────────────────────────────┘
                                      │
                     ┌────────────────▼────────────────┐
                     │  sqlserver (custom image)        │
                     │  └── WebhookDb (tokens, requests)│
                     └─────────────────────────────────┘

  ┌──────────────────────────────┐    ┌──────────────────────────────────┐
  │  SEQ (:5342 UI, localhost)   │    │  Nginx (:8088) + Angular SPA     │
  │  Structured log viewer       │    │  proxies /api /webhook /health   │
  └──────────────────────────────┘    └──────────────────────────────────┘
```

**4 Docker services:** `sqlserver`, `seq`, `api`, `frontend`

### 5.2 Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10, ASP.NET Core, Clean Architecture (Domain → Application → Infrastructure → API) |
| Frontend | Angular 21 (standalone components), Angular Material 3, SSE via EventSource |
| Database | SQL Server 2022 Developer Edition |
| Real-time | Server-Sent Events (in-process, no broker) |
| Logging | Serilog → SEQ |
| Container | Docker Compose (4 services) |
| Testing | xUnit + NSubstitute + FluentAssertions + Testcontainers + Playwright (backend); Vitest (Angular) |

---

## 6. Data Flows

### Flow A — Incoming Webhook Request

```
External caller: POST http://hostname:8088/webhook/{guid}
  Headers: { Content-Type: application/json }
  Body:    { "event": "order.created" }

1. Nginx (frontend container)
   proxy_set_header Host              $host
   proxy_set_header X-Real-IP         $remote_addr
   proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for
   proxy_pass http://api:8080

2. API middleware chain
   UseForwardedHeaders() → resolves real client IP into HttpContext.Connection.RemoteIpAddress

3. WebhookController
   EnableBuffering() — makes body seekable; called first before anything else

4. Read body unconditionally (no ContentLength gate — handles chunked requests correctly)
   Try/catch IOException → BadHttpRequestException (400) sent to GlobalExceptionMiddleware
   Binary Content-Type (image/*, application/octet-stream, etc.) → Base64-encode + IsBodyBase64=true
   Text Content-Type → read as string, IsBodyBase64=false

5. Size check: body bytes > MaxRequestSizeMb × 1024 × 1024 → 413

6. Token lookup: cache.Get($"token:{token}") or GetByTokenIncludingInactiveAsync(token)
   ⚠ GetByTokenIncludingInactiveAsync retrieves BOTH active AND inactive tokens (no IsActive filter)
   Cache miss → SELECT FROM WebhookTokens WHERE Token=@t
   Not found  → remove key from cache (never cache null), return 404

7. Capture: snapshot = { requestId, tokenId, method, path, queryString,
                         headers(JSON), body, isBase64, contentType,
                         ipAddress, userAgent, sizeBytes, receivedAt=UtcNow }

8. INSERT WebhookRequest to WebhookDb (~2–5 ms)
   ⚠ ALWAYS persists request, regardless of token active state (audit trail for inactive tokens)

9. Token state check:
   ├─ ACTIVE token:   SSE notify (best-effort, error-isolated):
   │                  try { await sseNotifier.NotifyAsync(tokenId, summaryDto) }
   │                  catch { log warning — request already durable, SSE is best-effort }
   │                  Return 200 OK or CustomResponse
   │
   └─ INACTIVE token: Return 410 Gone (signals sender to stop retrying)
                      Audit trail persisted in step 8; no SSE notification

   CustomResponse set → configured status + headers + body
   Not set            → 200 OK {"message": "Webhook received."}
   Active round-trip: ~5–15 ms | Inactive round-trip: ~3–8 ms
```

### Flow B — View Requests in SPA (SSE Live)

```
User opens token detail page
  → GET /api/tokens/{uuid}/requests?page=1&pageSize=20  (SummaryDto list, no body)
  → GET /api/tokens/{uuid}/sse                          (SSE subscribe)
      Nginx: proxy_buffering off; proxy_read_timeout 3600s
      API: atomic connection count check → 429 if >= 10 concurrent for this token
      First SSE frame: "retry: 5000\n\n"  (browser reconnect interval)
      Background timer sends ": ping\n\n" every 15s (keeps Nginx proxy alive)
  → New request arrives → event: request delivered immediately (in-process, O(1) TryWrite)
  → User clicks row → GET /api/tokens/{uuid}/requests/{reqId}  (DetailDto with body)
```

### Flow C — Configure Custom Response

```
User configures response in dialog
  → PUT /api/tokens/{uuid}/custom-response
     body: { statusCode: 201, contentType: "application/json",
             body: "{\"ok\":true}", headers: "{\"X-Custom\":\"value\"}" }
  → SetCustomResponseCommand handler:
      UPDATE WebhookTokens SET CustomResponse...
      _memoryCache.Remove($"token:{token.Token}")  ← explicit cache invalidation
  → Next incoming request uses new response immediately (cache miss forces DB read)
```

### Flow D — Token Deleted While SSE Connected

```
User deletes token
  → DeleteTokenCommand handler:
      soft-delete token (IsActive=false)
      hard-delete all requests for this token
      _memoryCache.Remove(tokenCacheKey)
      _sseNotifier.NotifyTokenDeleted(tokenId)
  → SseNotifier: writes "event: token-deleted\ndata: {}\n\n" to each channel
                 then calls Channel.Writer.Complete() on each
  → SPA EventSource receives "token-deleted" event → router.navigate(['/'])
```

### Flow E — Retention Cleanup

```
RetentionCleanupService (BackgroundService)
  → PeriodicTimer(24h) — first tick is 24h after startup
  → Creates a new IServiceScope per tick (avoids captive DbContext dependency)
  → if (RetentionDays <= 0) skip — keep forever
  → cutoff = UtcNow - RetentionDays
  → DELETE FROM WebhookRequests WHERE ReceivedAt < cutoff
  → log: "Retention cleanup deleted {Count} requests older than {Cutoff}"
  → DB errors are caught and logged; service continues on next tick regardless
```

---

## 7. Low-Level Design (LLD)

### 7.1 Domain Entities

**WebhookToken**

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` | PK — internal |
| `Token` | `Guid` | Unique indexed — public URL segment |
| `Description` | `string?` | Max 200 chars |
| `CreatedAt` | `DateTimeOffset` | UTC |
| `IsActive` | `bool` | Soft-delete flag |
| `CustomResponse` | `CustomResponse?` | EF Core owned entity; nullable |

**CustomResponse** (owned value object, columns in `WebhookTokens` table)

| Column | Type | Default |
|--------|------|---------|
| `StatusCode` | `int` | 200 |
| `ContentType` | `string` | `text/plain` |
| `Body` | `string?` | null |
| `Headers` | `string` | `"{}"` — raw JSON string |

> `CustomResponse.Headers` and the API request field `SetCustomResponseRequest.Headers` are both typed as `string` (raw JSON). The Angular dialog sends a JSON string, not a `Record<string,string>` object. Keep both sides in sync — see §16 Design Decisions (PA2).

**WebhookRequest**

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` | PK — generated by API before INSERT |
| `TokenId` | `Guid` | FK → `WebhookToken.Id`, indexed |
| `ReceivedAt` | `DateTimeOffset(7)` | Indexed — retention + pagination; **millisecond precision** (7-digit fractional seconds) |
| `Method` | `string` | Max 10 |
| `Path` | `string` | Max 2048 |
| `QueryString` | `string?` | Max 4096 |
| `Headers` | `string` | `NVARCHAR(MAX)` — JSON |
| `Body` | `string?` | `NVARCHAR(MAX)` — Base64 if binary |
| `IsBodyBase64` | `bool` | True when body is Base64-encoded |
| `ContentType` | `string?` | Max 256 |
| `IpAddress` | `string` | Max 45 (IPv6); real IP via `X-Forwarded-For` |
| `UserAgent` | `string?` | Max 512 |
| `SizeBytes` | `long` | |

**Indexes:** `WebhookToken.Token` (unique), `WebhookRequest.TokenId` (non-clustered), `WebhookRequest.ReceivedAt` (non-clustered).

### 7.2 DTOs

**`WebhookRequestSummaryDto`** — list endpoint (no body, no headers)

```
{ Id, TokenId, Method, Path, ReceivedAt, ContentType, SizeBytes, IpAddress }
```

**`WebhookRequestDetailDto`** — single-request GET (includes body and headers)

```
{ Id, TokenId, Method, Path, QueryString, ReceivedAt, ContentType,
  Headers (Dictionary<string,string>), Body, IsBodyBase64, SizeBytes, IpAddress, UserAgent }
```

**`WebhookTokenDto`**

```
{ Id, Token, WebhookUrl, Description, CreatedAt, IsActive, CustomResponse? }
```

**`PagedResult<T>`**

```
{ Items: T[], Page: int, PageSize: int, Total: int }
```

### 7.3 API Contract

#### Token Management

| Method | Path | Request Body | Response | Notes |
|--------|------|--------------|----------|-------|
| `GET` | `/api/tokens` | — | `200 WebhookTokenDto[]` | Active tokens only |
| `GET` | `/api/tokens/{id}` | — | `200 WebhookTokenDto` / `404` | |
| `POST` | `/api/tokens` | `{ description?: string }` | `201 WebhookTokenDto` | `webhookUrl` uses `WEBHOOK_BASE_URL` |
| `PUT` | `/api/tokens/{id}` | `{ description?: string, isActive: bool }` | `200 WebhookTokenDto` / `404` | Update description or reactivate |
| `DELETE` | `/api/tokens/{id}` | — | `204` / `404` | Soft-delete + hard-delete all requests |
| `PUT` | `/api/tokens/{id}/custom-response` | `{ statusCode, contentType, body?, headers }` | `204` / `404` | `headers` is a raw JSON string |
| `DELETE` | `/api/tokens/{id}/custom-response` | — | `204` / `404` | Resets to 200 OK defaults |

#### Request Management

| Method | Path | Response | Notes |
|--------|------|----------|-------|
| `GET` | `/api/tokens/{tokenId}/requests` | `200 PagedResult<SummaryDto>` | `?page=1&pageSize=20&search=foo`; max pageSize 100; `422` if invalid |
| `GET` | `/api/tokens/{tokenId}/requests/{id}` | `200 DetailDto` / `404` | Full body + headers |
| `GET` | `/api/tokens/{tokenId}/requests/{id}/export` | `200 application/json` / `404` | `Content-Disposition: attachment; filename="request-{id}.json"` |
| `DELETE` | `/api/tokens/{tokenId}/requests` | `204` | Clear all requests for token |
| `DELETE` | `/api/tokens/{tokenId}/requests/{id}` | `204` / `404` | |

#### Webhook Receiver

| Method | Path | Response | Notes |
|--------|------|----------|-------|
| `ANY` | `/webhook/{token:guid}` | **Active:** Custom or `200` \| **Inactive:** `410 Gone` | Accepts all HTTP methods; inactive tokens return 410 but persist request for audit trail |

#### SSE Stream

| Method | Path | Response |
|--------|------|----------|
| `GET` | `/api/tokens/{tokenId}/sse` | `200 text/event-stream` / `429` if ≥ 10 concurrent |

**SSE event shapes:**

```
retry: 5000

event: request
data: {"id":"...","method":"POST","path":"/webhook/...","receivedAt":"...","sizeBytes":...}

event: token-deleted
data: {}

: ping
```

> **Wire vs internal names:** The backend emits `event: request`. The Angular `SseService` listens via `es.addEventListener('request', handler)` and maps internally to `{ eventType: 'new-request' }`. The wire name `request` must never be renamed — doing so silently breaks real-time updates.

#### Validation Errors

All input validation failures return **HTTP 422** (not 400):

```json
{
  "errors": [
    { "field": "pageSize", "message": "'pageSize' must be less than or equal to 100." }
  ]
}
```

#### Error Handling (GlobalExceptionMiddleware)

| Exception | Response | Notes |
|-----------|----------|-------|
| `OperationCanceledException` (RequestAborted) | *silent* | Normal SSE/long-poll disconnect; not logged |
| `ValidationException` | **422** + field errors | FluentValidation failures |
| `BadHttpRequestException` | **400** or **413** | Body read errors, request size violations; logged with context |
| `Exception` (unhandled) | **500** | Logged with full context; error message not exposed to client |

All responses are guarded by `context.Response.HasStarted` check — essential for SSE safety (headers already flushed).

### 7.4 SSE Architecture

```
SseNotifier (singleton)
  ConcurrentDictionary<tokenId, ConcurrentDictionary<channelId, Channel<SseEvent>>>
  │
  ├── SubscribeAsync(tokenId, ct):
  │     lock(perTokenLock): if count >= 10 → throw TooManyConnectionsException → 429
  │     channelId = Guid.NewGuid()
  │     create bounded Channel<SseEvent>(capacity: 50)
  │     add to inner dict
  │     yield return each event from channel reader
  │     on cancellation → remove channel from dict
  │
  ├── NotifyAsync(tokenId, dto):
  │     foreach channel in tokenId's inner dict
  │       channel.Writer.TryWrite(...)  ← O(1), non-blocking
  │       silently drops if buffer full (request is already persisted in DB)
  │
  └── NotifyTokenDeleted(tokenId):
        write SseEvent("token-deleted", "{}") to each channel
        Channel.Writer.Complete() on each
        remove tokenId from outer dict
```

`NullSseNotifier` (no-op) is available for test substitution.

### 7.5 Application Layer — CQRS Map

**Token Commands**

| Command | Effect | Cache |
|---------|--------|-------|
| `CreateTokenCommand` | INSERT WebhookToken | — |
| `UpdateTokenCommand` | UPDATE description / IsActive | — |
| `DeleteTokenCommand` | Soft-delete + hard-delete requests + `NotifyTokenDeleted` | `Remove()` |
| `SetCustomResponseCommand` | UPDATE CustomResponse columns | `Remove()` |
| `ResetCustomResponseCommand` | Clear CustomResponse columns | `Remove()` |

**Request Commands**

| Command | Effect |
|---------|--------|
| `DeleteRequestCommand` | DELETE single request (verifies TokenId ownership) |
| `ClearRequestsCommand` | DELETE all requests for token |

**Token Queries**

| Query | Returns | Notes |
|-------|---------|-------|
| `GetTokensQuery` | `WebhookTokenDto[]` | `WHERE IsActive = 1` only |
| `GetTokenQuery` | `WebhookTokenDto?` | Returns null if inactive |

**Request Queries**

| Query | Returns | Notes |
|-------|---------|-------|
| `GetRequestsQuery` | `PagedResult<SummaryDto>` | LIKE search on Headers+Body; ownership check |
| `GetRequestByIdQuery` | `DetailDto?` | Full body; IDOR ownership check |
| `ExportRequestQuery` | `byte[]?` | JSON bytes; IDOR ownership check |

**Pipeline Behaviors (run on every command/query)**

| Behavior | What it does |
|----------|-------------|
| `LoggingBehavior` | Logs handler name + duration; never logs payloads |
| `ValidationBehavior` | Runs FluentValidation; throws `ValidationException` → 422 |

### 7.6 WebhookOptions & Startup Validation

```
WebhookOptions:
  BaseUrl        — REQUIRED, no fallback; app fails to start if absent/empty
  RetentionDays  — default 7; range 0–365; 0 = keep forever
  MaxRequestSizeMb — default 5; range 1–100
```

Validated at startup via `IValidateOptions<WebhookOptions>` with `.ValidateOnStart()`. The application throws and refuses to start if any value is invalid. There is no silent misconfiguration.

### 7.7 Token Cache Strategy

- `IMemoryCache` key `"token:{guid}"` with 5-minute sliding expiration
- Used by **both** active and inactive tokens (via `GetByTokenIncludingInactiveAsync`)
  - Receiver path needs quick inactive lookup to return 410 Gone without DB hit
  - Cache entry persists for token's full lifetime (active or inactive)
  - Only invalidated on mutation (not on IsActive status change)
- **Null tokens are never cached** — cache key explicitly removed when token not found
- All mutations call `_memoryCache.Remove($"token:{token.Token}")`:
  - `SetCustomResponseCommand`
  - `ResetCustomResponseCommand`
  - `DeleteTokenCommand`
  - `UpdateTokenCommand` (when IsActive toggled)

---

## 8. Solution Structure

```
WebhookService.sln
│
├── src/
│   ├── WebhookService.Domain/
│   │   ├── Entities/
│   │   │   ├── WebhookToken.cs
│   │   │   └── WebhookRequest.cs
│   │   ├── ValueObjects/
│   │   │   └── CustomResponse.cs
│   │   ├── Repositories/
│   │   │   ├── IWebhookTokenRepository.cs
│   │   │   └── IWebhookRequestRepository.cs    ← includes DeleteOlderThanAsync
│   │   └── Services/
│   │       ├── ISseNotifier.cs
│   │       └── SseEvent.cs                     ← record SseEvent(string EventName, string Data)
│   │
│   ├── WebhookService.Application/
│   │   ├── Tokens/Commands/
│   │   │   ├── CreateToken/
│   │   │   ├── UpdateToken/                    ← description + isActive
│   │   │   ├── DeleteToken/                    ← soft-delete + hard-delete + cache + SSE notify
│   │   │   ├── SetCustomResponse/              ← UPDATE + cache invalidate
│   │   │   └── ResetCustomResponse/            ← clear + cache invalidate
│   │   ├── Tokens/Queries/
│   │   │   ├── GetTokens/                      ← WHERE IsActive=1 only
│   │   │   └── GetToken/                       ← returns null if inactive
│   │   ├── Requests/Commands/
│   │   │   ├── DeleteRequest/
│   │   │   └── ClearRequests/
│   │   ├── Requests/Queries/
│   │   │   ├── GetRequests/                    ← PagedResult<SummaryDto>; LIKE search; pageSize≤100
│   │   │   ├── GetRequestById/                 ← DetailDto with body; IDOR check
│   │   │   └── ExportRequest/                  ← JSON bytes; IDOR check
│   │   ├── Common/Behaviors/
│   │   │   ├── LoggingBehavior.cs              ← name + duration only; no payload logging
│   │   │   └── ValidationBehavior.cs           ← FluentValidation → 422
│   │   ├── Common/DTOs/
│   │   │   ├── WebhookTokenDto.cs
│   │   │   ├── WebhookRequestSummaryDto.cs
│   │   │   └── WebhookRequestDetailDto.cs
│   │   ├── Common/Models/
│   │   │   └── PagedResult.cs
│   │   ├── Options/
│   │   │   ├── WebhookOptions.cs
│   │   │   └── WebhookOptionsValidator.cs      ← IValidateOptions; ValidateOnStart
│   │   └── DependencyInjection.cs
│   │
│   ├── WebhookService.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── ApplicationDbContext.cs
│   │   │   ├── Configurations/
│   │   │   │   ├── WebhookTokenConfiguration.cs    ← owned entity; column lengths; indexes
│   │   │   │   └── WebhookRequestConfiguration.cs  ← Path max 2048, QueryString max 4096
│   │   │   ├── Repositories/
│   │   │   │   ├── WebhookTokenRepository.cs       ← AsNoTracking; GetByTokenIncludingInactiveAsync (receiver path)
│   │   │   │   └── WebhookRequestRepository.cs     ← AsNoTracking; DeleteOlderThanAsync; ThenByDescending(Id)
│   │   │   └── Migrations/
│   │   ├── BackgroundServices/
│   │   │   └── RetentionCleanupService.cs      ← IServiceScopeFactory; try/catch; PeriodicTimer(24h)
│   │   ├── Sse/
│   │   │   ├── NullSseNotifier.cs              ← no-op; used in tests
│   │   │   └── SseNotifier.cs                  ← ConcurrentDictionary; atomic count; bounded channels
│   │   └── DependencyInjection.cs
│   │
│   └── WebhookService.API/
│       ├── Controllers/
│       │   ├── TokensController.cs
│       │   ├── RequestsController.cs
│       │   ├── WebhookController.cs            ← EnableBuffering; body reading; SSE notify
│       │   └── SseController.cs                ← atomic connection check; SSE loop; ping timer
│       ├── Middleware/
│       │   └── GlobalExceptionMiddleware.cs    ← BadHttpRequestException (400/413); 422 for ValidationException; SSE guard
│       └── Program.cs                          ← ForwardedHeaders; Kestrel limit; CORS; retry policy; Polly migrations
│
├── tests/
│   ├── WebhookService.UnitTests/
│   ├── WebhookService.IntegrationTests/
│   └── WebhookService.E2ETests/
│       └── Fixtures/
│           └── DockerComposeFixture.cs         ← GlobalSetup/Teardown; waits for health checks
│
├── frontend/
│   └── webhook-spa/src/app/
│       ├── services/
│       │   └── theme.service.ts         ← dark/light toggle; localStorage persistence; Angular signals
│       ├── core/
│       │   ├── models/                  ← token.model.ts, request-summary.model.ts, request-detail.model.ts
│       │   ├── services/                ← token.service.ts, request.service.ts, sse.service.ts
│       │   └── interceptors/            ← http-error.interceptor.ts
│       ├── features/
│       │   ├── dashboard/               ← token list + create
│       │   ├── token-detail/            ← split view: SSE-live request list + detail panel
│       │   └── custom-response/         ← dialog component
│       └── shared/
│           ├── copy-button.component.ts
│           ├── search-bar.component.ts  ← 300ms debounce; resets page to 1
│           ├── body-viewer.component.ts ← JSON pretty-print; Base64 decode
│           └── sse-status.component.ts  ← green/red dot
│
├── docker/
│   ├── frontend/
│   │   ├── Dockerfile                   ← multi-stage: Node build → Nginx Alpine
│   │   └── nginx.conf
│   └── sqlserver/
│       ├── Dockerfile                   ← wraps official image; adds entrypoint.sh
│       ├── entrypoint.sh                ← polls until SQL Server ready, then runs init.sql
│       └── init.sql                     ← CREATE DATABASE WebhookDb IF NOT EXISTS
│
├── Dockerfile                           ← API: multi-stage .NET SDK → ASP.NET 10 runtime
├── docker-compose.yml
├── docker-compose.override.yml          ← local dev: CORS for :4200, BaseUrl for :8088
└── .env.example
```

**Dependency direction:** `API → Application → Domain` and `Infrastructure → Domain`. Domain has zero external dependencies.

---

## 9. Docker Compose

### Services

| Service | Image | External Port | Purpose |
|---------|-------|--------------|---------|
| `sqlserver` | Custom (`docker/sqlserver/Dockerfile`) | `1433` | WebhookDb (SQL Server 2022 Developer) |
| `seq` | `datalust/seq:latest` | `127.0.0.1:5341` (ingest), `127.0.0.1:5342` (UI) | Structured logs — **localhost-only** |
| `api` | Custom (`./Dockerfile`) | `8080` | ASP.NET Core API + BackgroundService |
| `frontend` | Custom (`docker/frontend/Dockerfile`) | `8088` | Nginx — Angular SPA + reverse proxy |

> SEQ ports are bound to `127.0.0.1` and are inaccessible from external hosts by design. Access SEQ at `http://localhost:5342`.

### MSSQL Custom Image

The official SQL Server image does not reliably support schema initialization via environment variables. This project uses a custom image:

- `docker/sqlserver/Dockerfile` — wraps `mcr.microsoft.com/mssql/server:2022-latest`
- `docker/sqlserver/entrypoint.sh` — starts `sqlservr` in background, polls with `sqlcmd SELECT 1` until ready (~10–45 s), then executes `init.sql`
- `docker/sqlserver/init.sql` — `CREATE DATABASE WebhookDb IF NOT EXISTS`

### Nginx Configuration (`docker/frontend/nginx.conf`)

Key directives:

```nginx
# SSE stream — no buffering, long timeout
location ~ ^/api/tokens/[^/]+/sse$ {
    proxy_pass            http://api:8080;
    proxy_http_version    1.1;
    proxy_set_header      Connection "";
    proxy_buffering       off;
    proxy_cache           off;
    chunked_transfer_encoding on;
    proxy_read_timeout    3600s;
}

location /api/     { proxy_pass http://api:8080; }
location /webhook/ { proxy_pass http://api:8080; }
location /health   { proxy_pass http://api:8080; }

location / {
    root      /usr/share/nginx/html;
    index     index.html;
    try_files $uri $uri/ /index.html;
}
```

All proxy locations pass: `Host $host`, `X-Real-IP $remote_addr`, `X-Forwarded-For $proxy_add_x_forwarded_for`, `X-Forwarded-Proto $forwarded_scheme`.

`$forwarded_scheme` is defined in `docker/frontend/nginx-maps.conf` (loaded as `00-maps.conf`). It preserves an upstream `X-Forwarded-Proto` header (e.g. from ngrok) rather than using the local `$scheme`, ensuring the API sets the `Secure` cookie flag correctly behind a TLS-terminating proxy. The API's `ForwardedHeadersOptions.ForwardLimit` is `2` to accommodate the two-hop chain: ngrok → nginx → api.

### ngrok Tunnel (`docker-compose.ngrok.yml`)

Expose the stack publicly without port-forwarding:

```bash
# Add to .env:
# NGROK_AUTHTOKEN=<your_ngrok_token>
# WEBHOOK_BASE_URL=https://<your-ngrok-domain>

docker compose -f docker-compose.yml -f docker-compose.ngrok.yml up -d
```

This adds an `ngrok` service that tunnels `https://<domain> → frontend:80`. The ngrok inspector UI is available at `http://localhost:4041`.

### Development Override (`docker-compose.override.yml`)

```yaml
services:
  api:
    environment:
      ASPNETCORE_ENVIRONMENT: "Development"
      Cors__AllowedOrigins:   "http://localhost:4200"
      Webhook__BaseUrl:       "http://localhost:8088"
  sqlserver:
    ports:
      - "1433:1433"
```

Use with: `docker compose -f docker-compose.yml -f docker-compose.override.yml up -d`

---

## 10. Configuration Reference

All configuration is via environment variables.

| Variable | Required | Default | Valid Range | Description |
|----------|----------|---------|-------------|-------------|
| `SA_PASSWORD` | **Yes** | — | Strong password | SQL Server SA password |
| `WEBHOOK_BASE_URL` | **Yes** | — | Any non-empty URL | Base URL for generated webhook URLs (e.g. `http://192.168.1.10:8088`) |
| `AUTH_USERNAME` | **Yes** | — | Any non-empty string | Admin login username |
| `AUTH_PASSWORD_HASH` | **Yes** | — | BCrypt hash (`$2...`) | BCrypt hash of the admin password — never store plaintext |
| `RETENTION_DAYS` | No | `7` | 0–365 | Days to keep requests; `0` = keep forever |
| `MAX_REQUEST_SIZE_MB` | No | `5` | 1–100 | Max accepted request body size in MB |
| `AUTH_SESSION_HOURS` | No | `8` | 1–168 | Session cookie lifetime in hours |
| `WEBHOOK__ReceiverRateLimitPerSecond` | No | `250` | 1–10000 | Max requests per second per token on the webhook receiver; uses token-bucket algorithm |
| `Cors__AllowedOrigins` | No | `""` | Comma-separated origins | Leave empty in production (Nginx proxies all traffic) |
| `NGROK_AUTHTOKEN` | No | — | ngrok auth token | Required only when using `docker-compose.ngrok.yml` |

**Generating / rotating a BCrypt hash** — use the included rotation utility (recommended):

```bash
# Recommended: built-in .NET utility — prompts interactively
dotnet run --project tools/RotatePassword

# Non-interactive + auto-update .env in one step
dotnet run --project tools/RotatePassword -- --password "YourStr0ngP@ss!" --update-env .env
# Then restart the API: docker compose restart api

# Alternative: Python
python3 -c "import bcrypt; print(bcrypt.hashpw(b'YourStr0ngP@ss!', bcrypt.gensalt(12)).decode())"

# Alternative: Node.js (bcryptjs)
node -e "const b=require('bcryptjs'); console.log(b.hashSync('YourStr0ngP@ss!',12))"
```

**Startup validation:** The API process exits immediately at startup if `WEBHOOK_BASE_URL`, `AUTH_USERNAME`, or `AUTH_PASSWORD_HASH` are missing/invalid, or if `MAX_REQUEST_SIZE_MB`, `RETENTION_DAYS`, or `AUTH_SESSION_HOURS` are outside their valid ranges. Silent misconfiguration is worse than a fast failure.

---

## 11. Security Model

### Cookie-Based Authentication

All management endpoints require a valid session cookie. The webhook receiver (`/webhook/{token:guid}`) and health endpoints remain anonymous — external callers must not need credentials to deliver webhooks, and Docker needs the health endpoints for container orchestration.

**Credential storage:** The admin password is stored as a BCrypt hash in `AUTH_PASSWORD_HASH`. The application never sees or stores the plaintext password. BCrypt's inherent work factor provides protection against brute-force if the hash is ever exposed.

**Session cookie properties:**
- `HttpOnly` — not accessible from JavaScript; mitigates XSS token theft
- `SameSite=Strict` — blocks cross-site requests including safe ones (navigation); stricter than `Lax` for better security
- `Secure` flag follows `X-Forwarded-Proto` via `ForwardedHeaders` middleware — set to HTTPS in production
- Sliding expiration: `AUTH_SESSION_HOURS` (default 8 h)

**Why cookie auth, not Bearer JWT?** `EventSource` (used for SSE) cannot send custom headers. Cookies are sent automatically by the browser on every request, including SSE connections. Bearer tokens would require server-side changes to the SSE endpoint.

**Endpoint protection map:**

| Route | Auth |
|-------|------|
| `POST /api/auth/login` | Anonymous — login itself |
| `POST /api/auth/logout` | Anonymous — must work with expired/missing session |
| `GET /api/auth/me` | Requires session |
| `ANY /webhook/{token:guid}` | Anonymous — external callers |
| All other `/api/**` routes | Requires session (global fallback policy) |
| `GET /health/live`, `/health/ready` | Anonymous — Docker health checks |

**Deployment recommendations:**

- Bind the service to a non-public network interface, or place it behind a VPN
- Use firewall rules to restrict access to trusted IP ranges
- Do not expose port 8088 to the public internet
- SEQ (port 5342) is already `127.0.0.1`-bound — keep it that way
- Rotate credentials using `tools/RotatePassword/`: `dotnet run --project tools/RotatePassword -- --password "<new>" --update-env .env && docker compose restart api`

### Single-Admin Deployment Model

This application is designed for a **single administrator**. There is one login account (`AUTH_USERNAME` / `AUTH_PASSWORD_HASH`) and no per-user data isolation. All captured data is accessible to that admin account. This is intentional — the system is a personal/team webhook debugging tool, not a multi-tenant SaaS.

### Stored Data

Captured request data is persisted **unredacted** — this includes full headers (including `Cookie`, `Authorization`, and vendor HMAC secrets such as `X-Webhook-Secret`), full request bodies, and caller IP addresses. SEQ logs are also unfiltered. This is by design under the single-admin trust model. Ensure the deployment is network-access-controlled. The `RETENTION_DAYS` variable controls automatic cleanup.

### Additional Security Controls

- **CSRF protection:** Anti-forgery token via `X-XSRF-TOKEN` header. Angular's `HttpClient` reads the `XSRF-TOKEN` cookie and echoes the header automatically on state-changing requests.
- **Session revocation:** Logout invalidates the session server-side immediately via an in-memory revocation store. A server restart clears the revocation set (acceptable for single-admin use; re-login is required).
- **Rate limiting:** The webhook receiver is rate-limited per token; the login endpoint is rate-limited per real client IP (requires `X-Forwarded-For` from nginx).
- **Security headers:** nginx sets HSTS, CSP, Permissions-Policy, X-Frame-Options: DENY, X-Content-Type-Options: nosniff, and Referrer-Policy on all responses.

---

## 12. Development Guide

### Backend

**Requirements:** .NET 10 SDK, Docker (for integration tests)

```bash
# Start a local SQL Server (for development without full Docker Compose)
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=Dev@12345!" \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

# Build entire solution
dotnet build

# Format all C# files
dotnet format

# Run unit tests only (no Docker needed)
dotnet test tests/WebhookService.UnitTests/

# Run the API (configure appsettings.Development.json first)
dotnet run --project src/WebhookService.API
```

### Add an EF Core Migration

```bash
dotnet ef migrations add <MigrationName> \
  --project src/WebhookService.Infrastructure \
  --startup-project src/WebhookService.API

# Apply to local database
dotnet ef database update \
  --project src/WebhookService.Infrastructure \
  --startup-project src/WebhookService.API
```

In production (Docker), migrations are applied automatically at startup via Polly retry (5 attempts, exponential backoff, skips `OperationCanceledException`).

### Frontend

**Requirements:** Node.js 20+

```bash
cd frontend/webhook-spa

npm install

# Dev server at :4200 — proxies /api /webhook /health → localhost:8080
npm start

# Production build
npm run build

# Unit tests
npm test
```

The `proxy.conf.json` forwards to `http://localhost:8080` automatically. The .NET API must be running before starting `ng serve`.

### Full-Stack Development (Docker + Angular hot-reload)

```bash
# Start backend containers with CORS enabled for :4200
docker compose -f docker-compose.yml -f docker-compose.override.yml up -d

# Run Angular dev server (SSE connects through proxy.conf.json → API at :8080)
cd frontend/webhook-spa && npm start
```

### Upgrading an Existing Deployment

```bash
git pull
docker compose build
docker compose up -d
```

EF migrations run automatically at API startup — no manual DB steps. Data volumes persist across restarts. To also wipe data: `docker compose down -v`.

---

## 13. Testing

### Unit Tests — No Dependencies

```bash
dotnet test tests/WebhookService.UnitTests/
```

Covers domain entity invariants, CQRS handler logic, SSE notifier behaviour (11th connection throws, `token-deleted` completes channels), and `RetentionCleanupService` (DB errors do not stop the timer).

### Integration Tests — Requires Docker

Testcontainers pulls `mcr.microsoft.com/mssql/server:2022-latest` automatically. Docker must be running.

```bash
dotnet test tests/WebhookService.IntegrationTests/
```

Covers all API endpoints, pagination, LIKE search, IDOR ownership checks, chunked body capture, SSE limits, and health checks. Uses `WebApplicationFactory<Program>` + real SQL Server container. SSE is stubbed with `TestNullSseNotifier` — the real in-process notifier is not suited for isolated test assertions because it requires live SSE subscribers.

### Angular Unit Tests — No Dependencies

```bash
cd frontend/webhook-spa
npm test
```

Runs with **Vitest** via `@angular/build:unit-test`. Covers Angular component creation, template assertions, and services (e.g., `ThemeService` — dark/light toggle, localStorage persistence, error fallback). Uses `vi.stubGlobal('localStorage', ...)` for test isolation because Angular's Vitest environment provides a custom localStorage that bypasses `Storage.prototype`.

### E2E Tests — Full Stack Required

```bash
# Step 1: Build (required — playwright.ps1 only exists after build)
dotnet build tests/WebhookService.E2ETests/

# Step 2: Install Playwright browsers (first run only)
pwsh tests/WebhookService.E2ETests/bin/Debug/net10.0/playwright.ps1 install

# Step 3: Ensure Docker Compose stack is running
docker compose up -d

# Step 4: Run E2E tests
$env:E2E_BASE_URL="http://localhost:8088"
$env:E2E_AUTH_PASSWORD="admin"  # Must match AUTH_PASSWORD (plaintext) in .env
dotnet test tests/WebhookService.E2ETests/
```

Covers: dashboard load, token creation, real-time SSE appearance, SSE status indicator, custom response configuration, request export, and token deletion with navigation.

### Run All Tests

```bash
dotnet test
```

Running all tests requires Docker (integration) and the stack running with `E2E_BASE_URL` set (E2E). Run suites individually during development for faster feedback.

---

## 14. Observability

Structured logs are emitted via **Serilog** and forwarded to **SEQ** at `http://localhost:5342`.

| Event | Level | Structured Properties |
|-------|-------|----------------------|
| Webhook received | Information | `TokenId`, `Method`, `Path`, `IpAddress`, `SizeBytes` |
| SSE notification failed | Warning | `TokenId`, exception message |
| Retention cleanup ran | Information | `DeletedCount`, `CutoffDate` |
| MediatR handler completed | Information | `RequestType`, `DurationMs` |
| Validation failure | Warning | `RequestType`, field-level errors |
| Startup migration retry | Warning | attempt number, delay seconds |
| SSE client disconnected | (silent) | `OperationCanceledException` when `RequestAborted` is true — not logged as error |

**Useful SEQ queries:**

```
# All activity for a specific token
TokenId = "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"

# All slow handlers (>100ms)
DurationMs > 100

# Retention cleanup history
@Message like 'Retention cleanup%'
```

---

## 15. Contributing

### Branch Strategy

- `main` — always deployable
- Feature branches: `feat/<short-description>`
- Bug fix branches: `fix/<short-description>`

### Commit Format

```
<type>: <description>

<optional body>
```

Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `ci`

### Adding a New CQRS Command

1. Create `src/WebhookService.Application/<Feature>/Commands/<Name>/`
2. Add `<Name>Command.cs` (record implementing `IRequest<T>`)
3. Add `<Name>CommandHandler.cs` (implementing `IRequestHandler`)
4. Add `<Name>CommandValidator.cs` (`AbstractValidator<T>`)
5. Add a controller action — MediatR auto-discovers handlers via assembly scanning
6. Add unit tests in `tests/WebhookService.UnitTests/Application/`

### Adding a New EF Core Migration

```bash
dotnet ef migrations add <Name> \
  --project src/WebhookService.Infrastructure \
  --startup-project src/WebhookService.API
```

Review the generated migration before committing. Destructive changes (column drops, renames) need manual data safety review.

### PR Checklist

- [ ] `dotnet build` passes with 0 errors
- [ ] `dotnet format` applied
- [ ] Unit tests added/updated and passing (`dotnet test` for backend, `npm test` in `frontend/webhook-spa` for Angular)
- [ ] If touching API contract: §7.3 table updated
- [ ] If adding an env var: §10 Configuration Reference updated
- [ ] If changing SSE wire name or `headers` contract type: §7.3 and §16 PA2 note updated
- [ ] No secrets, credentials, or PII in committed files
- [ ] `docker compose up -d` boots clean

---

## 16. Design Decisions

### 16.1 Confirmed Decisions

| # | Topic | Decision |
|---|-------|----------|
| 1 | Real-time delivery | SSE (one-way push per token stream) — no WebSocket, no polling |
| 2 | Data retention | Configurable via `RETENTION_DAYS`; `BackgroundService` cleans daily |
| 3 | Custom response | Static per token (status + headers + body) |
| 4 | Request replay | Not supported — capture and inspect only |
| 5 | Token format | UUID — `/webhook/{guid}` |
| 6 | URL grouping | Flat list — no grouping |
| 7 | Search | `LIKE '%term%'` on headers + body; max `pageSize=100` |
| 8 | Request size limit | Configurable via `MAX_REQUEST_SIZE_MB`; enforced in Kestrel |
| 9 | Export | JSON per-request download |
| 10 | Background jobs | No Hangfire — `BackgroundService` + `PeriodicTimer(24h)` |
| 11 | SSE notify | Direct in-process call; best-effort; wrapped in try/catch |
| 12 | Database | Single `WebhookDb` on SQL Server 2022 Developer Edition |
| 13 | SEQ | Container in docker-compose; ports bound to `127.0.0.1` |
| 14 | MSSQL init | Custom Docker image with `entrypoint.sh` + `sqlcmd` polling loop |
| 15 | Scale target | < 100 webhook URLs, small team, single API instance |
| 16 | Frontend | Separate Nginx container serving Angular build |
| 17 | Testing | Unit + Integration (Testcontainers) + E2E (Playwright) |
| 18 | Redis | Future upgrade for multi-instance SSE; `ISseNotifier` interface unchanged |
| 19 | Binary payloads | Base64-encoded; `IsBodyBase64` flag; ~33% storage overhead |
| 20 | SSE notify timing | `TryWrite` to bounded Channel — O(1), non-blocking |
| 21 | IP capture | `UseForwardedHeaders()` in pipeline; Nginx passes `X-Real-IP` + `X-Forwarded-For` |
| 22 | `WebhookOptions` | Validated at startup via `IValidateOptions`; fail-fast on invalid config |

### 16.2 Revision History

| Version | Date | Summary |
|---------|------|---------|
| v1 | 2026-05-04 | Initial plan |
| v2 | 2026-05-04 | Devil's advocate review — 5 blocking + 5 high + 8 medium issues fixed |
| v3 | 2026-05-04 | Hangfire removed; 6→4 Docker services; SSE notify is direct in-process; BackgroundService for retention |
| v4 | 2026-05-04 | Second review — IP forwarding, BackgroundService crash guard, NullSseNotifier, body reading, CORS, ports, Host header, SSE race condition, options validation |
| v5 | 2026-05-04 | Code review — route corrections, IDOR fixes, cache eviction, AsNoTracking, 422 validation, SSE retry frame, nginx updates, Polly retry narrowing |
| v6 | 2026-05-05 | Browser + SEQ audit — 3 bugs fixed: GlobalExceptionMiddleware SSE disconnect crash, custom response headers type mismatch, HasStarted guard on ValidationException |

### 16.3 All Issues Found & Fixed

**Blocking**

| # | Issue | Fix |
|---|-------|-----|
| B1 | Hangfire job carried full request body | Resolved — Hangfire removed (v3) |
| B2 | Token cache not invalidated on CustomResponse update | Explicit `_memoryCache.Remove()` in SetCustomResponse, ResetCustomResponse, DeleteToken |
| B3 | MSSQL Docker `init.sql` not supported by MSSQL image | Custom Dockerfile + `entrypoint.sh` polling loop + `sqlcmd` |
| B4 | Request body stream consumed by middleware before controller | `Request.EnableBuffering()` as first line in `WebhookController` |
| B5 | Kestrel `MaxRequestBodySize` not set from env var | `ConfigureKestrel()` in `Program.cs` + `[RequestSizeLimit]` attribute |
| BN1 | All captured IPs were Nginx's Docker internal IP | Nginx passes `X-Real-IP` + `X-Forwarded-For`; API calls `UseForwardedHeaders()` early |
| BN2 | `RetentionCleanupService` stops permanently on first DB error | try/catch wraps entire cleanup body; service continues on next tick |
| BN3 | `ISseNotifier.NotifyAsync()` called before implementation existed | `NullSseNotifier` (no-op) registered from Day 1 |
| BN4 | Body silently empty for chunked requests (no `Content-Length`) | Body reading is unconditional — ContentLength gate removed |

**High Priority**

| # | Issue | Fix |
|---|-------|-----|
| H4 | List endpoint returned full body — 200 MB responses possible | `SummaryDto` (no body) for list; `DetailDto` (with body) for single-item GET |
| H5 | CORS not configured for Angular dev server | CORS from `Cors__AllowedOrigins` env var; override adds `:4200` |
| HN1 | `"".Split(',')` produces `[""]` — empty string added as CORS origin | `StringSplitOptions.RemoveEmptyEntries | TrimEntries` |
| HN2 | API container port was image-version-dependent | `ASPNETCORE_HTTP_PORTS: "8080"` explicit in docker-compose |
| HN3 | `proxy_set_header Host` missing — generated URLs showed `http://api:8080/...` | Nginx passes `Host $host`; `WEBHOOK_BASE_URL` required at startup |
| HN4 | SSE connection count check was TOCTOU race | Per-tokenId lock; check + increment in one critical section |
| HN5 | `WebhookOptions` not validated at startup | `IValidateOptions<WebhookOptions>` + `.ValidateOnStart()` |

**Medium**

| # | Issue | Fix |
|---|-------|-----|
| M1 | SSE stream hangs when token is deleted | `token-deleted` SSE event + `Channel.Writer.Complete()` on delete |
| M2 | No SSE connection limit per token | Max 10 concurrent; 11th → 429 |
| M3 | Binary payloads corrupt in `NVARCHAR(MAX)` | Base64-encode; `IsBodyBase64` flag |
| M4 | Search does not reset pagination | Angular resets to page 1 when search term changes |
| M5 | No `pageSize` ceiling | FluentValidation enforces `pageSize` ≤ 100 |
| MN1 | `NotifyAsync` called without try/catch in WebhookController | Wrapped in try/catch; 200 still returned (request is durable) |
| MN2 | `RetentionCleanupService` injects scoped `DbContext` into singleton | Uses `IServiceScopeFactory`; new scope per cleanup tick |
| MN3 | Token cache stampede possible | `GetOrCreateAsync` with sliding expiry; null never cached |
| MN4 | `Path` and `QueryString` were unbounded `NVARCHAR(MAX)` | EF config: Path max 2048, QueryString max 4096 |
| MN5 | `SseEvent` record missing from solution | Added to `Domain/Services/SseEvent.cs` |
| MN6 | `GetTokensQuery` filter not documented | Handler explicitly filters `WHERE IsActive = 1` |
| MN7 | E2E tests require Docker Compose v2 `--wait` flag | Prerequisite documented |
| MN8 | No local dev DB guidance | `docker run` command added to development guide |

**Post-Audit Bugs (found during live browser + SEQ audit, v6)**

| # | Issue | Fix |
|---|-------|-----|
| PA1 | `GlobalExceptionMiddleware` crashed on SSE client disconnect — `OperationCanceledException` reached the generic `Exception` handler, which called `WriteErrorAsync`, which tried to set `StatusCode` on an already-started SSE response → `InvalidOperationException` | Added `catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)` silent branch; `if (context.Response.HasStarted) return;` guard in `WriteErrorAsync`; same guard on `ValidationException` branch |
| PA2 | `SetCustomResponseDto.headers` type mismatch — Angular sent a `Record<string,string>` object but C# API expected a raw JSON `string` — every custom response save returned HTTP 400 | Changed `SetCustomResponseDto.headers` to `string`; dialog `save()` passes the raw textarea string directly (not `JSON.parse(headersJson)`) |

---

## 17. Trade-offs & Known Limitations

| Limitation | Impact | Acceptable Because |
|------------|--------|--------------------|
| `LIKE` search — full scan per token | Slow for tokens with 10k+ requests | < 100 URLs, small team; FTS upgrade path documented |
| SSE notify is best-effort — full channel buffer → event dropped | User may miss a live event; refresh restores it | Request is durable in DB |
| Single API instance — SSE channels are in-process | Horizontal scaling breaks SSE | Redis upgrade path documented; interface unchanged |
| Binary payloads Base64-encoded — ~33% storage overhead | Larger DB rows | Max 5–10 MB per request, small scale |
| `PeriodicTimer(24h)` resets on restart | Cleanup may run slightly late after restart | Daily timing is not critical |
| Soft-deleted tokens remain in DB | DB accumulates inactive rows | Negligible at < 100 URL scale |
| `RetentionCleanupService` first tick is 24h after startup | No cleanup on day of startup | Acceptable |
| No bulk export (all requests for a token) | Out of scope | Documented as future path |
| Single admin account | No per-user roles or multi-user support | Sufficient for single-operator self-hosted use; multi-user path: add user table + roles |

---

## 18. Future Roadmap

| Feature | Trigger | Migration Path |
|---------|---------|----------------|
| Redis for SSE + distributed cache | API needs horizontal scaling | Replace `SseNotifier` with Redis Pub/Sub; replace `IMemoryCache` with `IDistributedCache`. `ISseNotifier` interface unchanged. |
| Multi-user / RBAC | Multiple operators with separate credentials | Add user table + roles; replace single `AuthOptions` with a user store. Domain/Application layers unchanged. |
| Full-Text Search | `LIKE` too slow at scale | Add FTS catalog in EF migration; replace `LIKE` with `EF.Functions.Contains()`. One handler change. |
| Bulk export | User request | `GET /api/tokens/{uuid}/requests/export` returning NDJSON or ZIP. |
| Hangfire | Complex scheduled jobs needed | Add `WebhookService.Worker` project; move `RetentionCleanupService` to recurring Hangfire job. |

---

## 19. Access Points

| Service | URL | Notes |
|---------|-----|-------|
| Web UI | http://localhost:8088 | Angular SPA served by Nginx |
| Login page | http://localhost:8088/login | First stop if unauthenticated; guard redirects here automatically |
| API (direct) | http://localhost:8080 | Bypasses Nginx; useful for dev/debug |
| API Swagger | http://localhost:8080/swagger | OpenAPI explorer |
| SEQ Logs | http://localhost:5342 | Localhost-only; not accessible from other hosts |
| API Health (liveness) | http://localhost:8080/health/live | Always 200 if process is up |
| API Health (readiness) | http://localhost:8080/health/ready | 200 if SQL Server reachable |
| SQL Server | `localhost:1433` | Exposed in `docker-compose.override.yml` only |

---

## 20. Troubleshooting

### SQL Server container stays unhealthy after startup

SQL Server takes up to 45 s on first boot. The API retries automatically. Check logs:

```bash
docker compose logs sqlserver
```

Look for `Initialisation complete.` in the output. If absent, verify `SA_PASSWORD` meets SQL Server's complexity requirements (≥ 8 chars, upper + lower + digit + symbol).

### Generated webhook URLs show the wrong hostname

`WEBHOOK_BASE_URL` must be the address that external callers can reach, including the port. For local dev: `http://localhost:8088`. For LAN: `http://192.168.1.10:8088`. Not the internal container name (`http://api:8080`).

### SSE does not update in real time

1. Verify Nginx has `proxy_buffering off` on the `~ ^/api/tokens/[^/]+/sse$` location
2. If using Angular dev server at `:4200`, SSE goes through `proxy.conf.json` → port 8080; the .NET API must be running
3. Verify the Angular `SseService` listens for `event: request` (not `event: new-request`)
4. Open browser DevTools → Network → filter "EventSource" to see raw SSE frames

### Custom response save returns HTTP 400

The `headers` field must be a **JSON string** (`"{\"X-Custom\": \"value\"}"`) — not a JavaScript object. The dialog must pass the raw textarea string directly to the API, not `JSON.parse(rawHeaders)`. See §16 design decision PA2.

### Port conflicts

| Port | Service | Resolution |
|------|---------|-----------|
| 8088 | Nginx frontend | Change `ports` in `docker-compose.yml` frontend service |
| 8080 | API | Change `ports` in `docker-compose.yml` api service |
| 1433 | SQL Server | Only exposed in `docker-compose.override.yml` — omit override to avoid conflict |
| 5341/5342 | SEQ | Bound to `127.0.0.1` — no external conflict possible |

### Integration tests fail with container errors

Docker must be running. Testcontainers pulls `mcr.microsoft.com/mssql/server:2022-latest` on first run — allow time for this on a slow connection.

### E2E tests fail with `playwright.ps1 not found`

Run `dotnet build tests/WebhookService.E2ETests/` first. The `playwright.ps1` script is only present after the project is built.

### API returns 401 on all requests / app redirects to login immediately

The API fails startup validation if `AUTH_USERNAME` or `AUTH_PASSWORD_HASH` are missing or invalid:

1. Confirm both vars are set in `.env`
2. Verify `AUTH_PASSWORD_HASH` starts with `$2` (BCrypt prefix) — plaintext passwords will be rejected
3. Check API startup logs: `docker compose logs api` — look for `ValidateOptionsResult.Fail` messages
4. Regenerate the hash if unsure: `python3 -c "import bcrypt; print(bcrypt.hashpw(b'YourPassword', bcrypt.gensalt(12)).decode())"`

### Build errors after pulling updates

```bash
dotnet restore
cd frontend/webhook-spa && npm install
```
