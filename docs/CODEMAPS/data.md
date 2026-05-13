<!-- Generated: 2026-05-13 | Files scanned: 6 migrations + 2 EF configurations | Token estimate: ~750 -->

# Data

## Persistence Layers

| Layer | Engine | Use |
|---|---|---|
| Primary store | SQL Server 2022 | `WebhookTokens`, `WebhookRequests`, EF Core 10 |
| Hot path cache | Redis 7 | `token:{guid}` (5min sliding), session revocation set |
| Async queue | Redis Streams | `webhook-requests` (XADD → XREADGROUP → XACK) |
| SSE pub/sub | Redis Pub/Sub | `sse:{tokenId}` channels |

## Tables (EF Core / SQL Server)

### `WebhookTokens`
```
Id            UNIQUEIDENTIFIER  PK
Token         UNIQUEIDENTIFIER  IX_Unique
Name          NVARCHAR(80)      not null
Description   NVARCHAR(200)     null
CreatedAt     DATETIMEOFFSET
IsActive      BIT               default 1, IX_Active_CreatedAt (filtered IsActive=1)

  ┌─ Owned: CustomResponse ────────────────┐
  │  StatusCode    INT               null   │
  │  ContentType   NVARCHAR(100)     null   │
  │  Body          NVARCHAR(MAX)     null   │
  │  Headers       NVARCHAR(MAX)     null   │  (raw JSON string contract)
  └─────────────────────────────────────────┘
```

### `WebhookRequests`
```
Id              UNIQUEIDENTIFIER  PK
TokenId         UNIQUEIDENTIFIER  FK→WebhookTokens.Id, IX_TokenId_ReceivedAt_Id (covering)
Method          NVARCHAR(10)
Path            NVARCHAR(2048)
QueryString     NVARCHAR(4096)    null
Headers         NVARCHAR(MAX)              (JSON serialized headers map)
Body            NVARCHAR(MAX)     null
SizeBytes       INT
IpAddress       NVARCHAR(45)
IpCountry       NVARCHAR(2)        null
UserAgent       NVARCHAR(512)     null
ContentType     NVARCHAR(200)     null
ReceivedAt      DATETIMEOFFSET(3)            (precision pinned to 3, see migration #2)
ProcessingTimeMs BIGINT           null      ([JsonInclude] on private-set property)
Note            NVARCHAR(2000)    null
ResponseStatusCode INT            null
```

## Migrations (`src/Hookbin.Infrastructure/Migrations/`)
| Order | File | Effect |
|---:|---|---|
| 1 | `20260504115509_InitialCreate.cs` | Initial schema (Tokens + Requests + CustomResponse owned) |
| 2 | `20260506202000_PinReceivedAtPrecision.cs` | `DATETIMEOFFSET(3)` precision pin |
| 3 | `20260507041721_AddCoveringIndexForPaginatedRequests.cs` | `IX_TokenId_ReceivedAt_Id` covering index for pagination |
| 4 | `20260510104619_AddProcessingTimeMs.cs` | `ProcessingTimeMs` (populated by StreamWorker) |
| 5 | `20260510104653_AddRequestNote.cs` | `Note` (user-set via PATCH `/api/.../requests/{id}/note`) |
| 6 | `20260511160402_AddTokenNameAndRequestResponseAndCountry.cs` | `Name` on WebhookToken, `ResponseStatusCode` + `IpCountry` on Request |

**Migration runner:** API only (`MigrateAsync` in `Program.cs`). Workers poll `CanConnectAsync` and never migrate — enforced by architecture test `StreamWorker_DoesNotCallMigrateAsync` / `JobsWorker_DoesNotCallMigrateAsync`.

## EF Core Configurations
- `Persistence/Configurations/WebhookTokenConfiguration.cs`
  - `Token` unique index
  - `IsActive` filtered index (`IsActive = 1`)
  - `CustomResponse` mapped as `OwnsOne` (column-prefixed, all nullable)
- `Persistence/Configurations/WebhookRequestConfiguration.cs`
  - `(TokenId, ReceivedAt DESC, Id DESC)` covering index — drives paginated reads
  - `Headers` stored as JSON string (`NVARCHAR(MAX)`)

## Read Path
- Both repositories use `.AsNoTracking()` on every SELECT — enforced by `ZeroTrustInvariantsTests.WebhookTokenRepository_ReadMethods_AllUse_AsNoTracking`
- `WebhookRequestRepository`: orders by `ReceivedAt DESC, Id DESC` for deterministic pagination
- IDOR: all `GetRequestById` / `ExportRequest` / `DeleteRequest` include `WHERE TokenId = @tokenId`

## Token Cache (Redis)
```
Key:       token:{guid}                    (TokenId, not Token GUID — token is value)
Value:     JSON-serialized WebhookToken (includes CustomResponse)
TTL:       5 minutes sliding
Null:      NEVER cached — explicit Remove on miss
Invalidation: SetCustomResponse, ResetCustomResponse, UpdateToken, DeleteToken
            → all call `cache.Remove(tokenId)` after mutation
```

## Redis Stream `webhook-requests`
```
Producer:  RedisStreamPublisher (API)        XADD * payload {json}
Consumer:  RedisStreamConsumerService (Worker)
  - Consumer group: hookbin-stream
  - Consumer name : $HOOKBIN_WORKER_ID (stable across restarts)
  - Cold start    : XREADGROUP "0-0" (PEL recovery) → XREADGROUP ">" (new)
  - Ack           : XACK after AddAsync succeeds
  - On crash before XACK: re-read from PEL next start
```

## Redis Pub/Sub `sse:{tokenId}`
```
Publisher: StreamWorker (after persist)   PUBLISH sse:{tokenId} {summary-json}
Subscriber: API RedisSseBridgeService     SUBSCRIBE sse:*
            → parse channel → SseNotifier.NotifyAsync(tokenId, summary)
            → fan-out into per-connection Channel<SseEvent>
```

## Session Revocation Set
```
Key:       session:revoked
Type:      SET<string sessionId>
Logout:    SADD session:revoked {currentSessionId}
Auth chk:  SISMEMBER session:revoked {sessionId} on every request
```

## Retention
- `RetentionCleanupService` (JobsWorker): `PeriodicTimer(24h)`
- Deletes `WebhookRequests` older than `WebhookOptions.RetentionDays` (default 7)
- Batched in 5,000-row deletes, separate SQL command per batch
- Early-return if `RetentionDays <= 0`
- **Single replica only** — `deploy.replicas: 1` (no leader election)

## Data Flow Cross-Reference
- Receive flow:    `architecture.md` § "Webhook Receive Flow"
- SSE flow:        `architecture.md` § "Webhook Receive Flow" (last 3 lines)
- Custom-response cache invalidation:    `backend.md` CQRS table § "Tokens/Commands/SetCustomResponse"
