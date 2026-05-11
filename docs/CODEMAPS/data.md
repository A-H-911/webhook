<!-- Generated: 2026-05-11 | Verified: 5 migrations (InitialCreate through AddRequestNote); ProcessingTimeMs + Note columns in WebhookRequests; covering index IX_WebhookRequests_TokenId_ReceivedAt_Id; WebhookTokenRepository.UpdateAsync fix for EF Core owned entities -->

# Data Architecture

## Database
SQL Server 2022 (Docker) — single database `WebhookDb` with resilience: EnableRetryOnFailure(3, 2s)

## Tables

### WebhookTokens
```
Id                          UNIQUEIDENTIFIER  PK
Token                       UNIQUEIDENTIFIER  NOT NULL  UNIQUE INDEX
Description                 NVARCHAR(200)     NULL
CreatedAt                   DATETIMEOFFSET    NOT NULL
IsActive                    BIT               NOT NULL
CustomResponse_StatusCode   INT               NULL  (owned entity inline)
CustomResponse_ContentType  NVARCHAR(256)     NULL
CustomResponse_Body         NVARCHAR(MAX)     NULL
CustomResponse_Headers      NVARCHAR(MAX)     NULL  (raw JSON string)
```

### WebhookRequests
```
Id              UNIQUEIDENTIFIER  PK
TokenId         UNIQUEIDENTIFIER  NOT NULL  FK → WebhookTokens(Id) CASCADE DELETE
ReceivedAt      DATETIMEOFFSET(7) NOT NULL  INDEX  (⚠ millisecond precision via 20260506202000_PinReceivedAtPrecision)
Method          NVARCHAR(10)      NOT NULL
Path            NVARCHAR(2048)    NOT NULL
QueryString     NVARCHAR(4000)    NULL
Headers         NVARCHAR(MAX)     NOT NULL  (JSON string)
Body            NVARCHAR(MAX)     NULL
IsBodyBase64    BIT               NOT NULL
ContentType     NVARCHAR(256)     NULL
IpAddress       NVARCHAR(45)      NOT NULL  (supports IPv6)
UserAgent       NVARCHAR(512)     NULL
SizeBytes       BIGINT            NOT NULL
ProcessingTimeMs BIGINT           NULL      (set by StreamWorker after DB persist; null until processed)
Note            NVARCHAR(2000)    NULL      (user-editable per-request note via PATCH /note)

IX_WebhookRequests_TokenId                             (lookup by token)
IX_WebhookRequests_ReceivedAt                          (ordering)
IX_WebhookRequests_TokenId_ReceivedAt_Id  (covering)   (added 20260507; eliminates key lookup for paginated list)
Primary ordering: ReceivedAt DESC, THEN Id DESC (deterministic pagination)
```

## Relationships
```
WebhookTokens (1) ──< WebhookRequests (many)
  FK: WebhookRequests.TokenId → WebhookTokens.Id
  ON DELETE CASCADE
```

## EF Core Notes
- `CustomResponse` mapped as owned entity (inline columns, no separate table)
- All reads use `.AsNoTracking()`
- Migrations: `src/WebhookService.Infrastructure/Migrations/`
- Entity mutable properties use `private set;` — callers must use mutation methods (`Activate`, `Deactivate`, `UpdateDescription`, `SetCustomResponse`, `ClearCustomResponse`, `RecordProcessingTime`). EF Core reads/writes via reflection — no `PropertyAccessMode` override needed.
- **⚠ Owned Entity Update Invariant:** `CurrentValues.SetValues(source)` does NOT propagate to `OwnsOne`-mapped entities. After tracking via `FindAsync()`, use the entity's mutation methods to update owned properties. `SetValues()` is unsafe for aggregate roots with owned relationships.

### Migration History
| Migration | Date | Change |
|-----------|------|--------|
| `20260504115509_InitialCreate` | 2026-05-04 | Initial schema (WebhookTokens, WebhookRequests, CustomResponse owned entity) |
| `20260506202000_PinReceivedAtPrecision` | 2026-05-06 | `ReceivedAt` pinned to `datetimeoffset(7)` for millisecond precision |
| `20260507041721_AddCoveringIndexForPaginatedRequests` | 2026-05-07 | Covering index `IX_WebhookRequests_TokenId_ReceivedAt_Id` |
| `20260510104619_AddProcessingTimeMs` | 2026-05-10 | `ProcessingTimeMs BIGINT NULL` column |
| `20260510104653_AddRequestNote` | 2026-05-10 | `Note NVARCHAR(2000) NULL` column |

## Retention (Updated 2026-05-10)
`RetentionCleanupService` runs in the **`WebhookService.JobsWorker`** process (not the API).
Deletes requests older than `Webhook:RetentionDays` on a 24-hour `PeriodicTimer`.
Cleanup is batched: delete 5k rows per loop iteration (prevents timeout on large datasets).
Inactive tokens' requests are retained for audit trail.
`jobs-worker` must run as **single replica only** — no leader election exists.

## Request Search (Updated 2026-05-07)
Dashboard search box filters requests by:
- Method (exact match): "GET", "POST", etc.
- Path (substring): "/api/users"
- IpAddress (substring): "192.168" (supports IPv6)
- Minimum length: 2 characters to prevent broad scans

## Token Cache
`ITokenCache` (→ `RedisTokenCache`, backed by `IMemoryCache`) key `"token:{guid}"` — 5-minute sliding expiration.
`GetByTokenIncludingInactiveAsync` retrieves both active and inactive tokens (used by receiver path).
`ITokenCache.Remove(tokenId)` called by all four token mutation handlers.
Null results are never cached.

## Redis Stream Schema
```
Stream key: webhook-requests
Consumer group: webhook-api
Entry fields:
  payload  — JSON-serialized WebhookRequest (method, path, queryString, headers, body, ipAddress, tokenId, receivedAt)
Consumer name: WEBHOOK_WORKER_ID env var, fallback "consumer-{MachineName}"
PEL recovery: XREADGROUP "0-0" drains unACKed entries from previous run on startup
```

## Webhook Receiver Path
- Active token: persists request, notifies SSE subscribers, returns 200 (or custom response)
- Inactive token: persists request for audit trail, returns 410 Gone (signals sender to stop)
