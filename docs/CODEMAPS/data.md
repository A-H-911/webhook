<!-- Generated: 2026-05-07 | Updated: batched retention cleanup (5k rows/loop), search constraints on Method/Path/IpAddress -->

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

IX_WebhookRequests_TokenId                 (lookup by token)
IX_WebhookRequests_ReceivedAt              (ordering)
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
- Initial migration: `20260504115509_InitialCreate`

## Retention (Updated 2026-05-07)
`RetentionCleanupService` deletes requests older than `Webhook:RetentionDays` on a 24-hour `PeriodicTimer`. 
Cleanup is batched: delete 5k rows per loop iteration (prevents timeout on large datasets).
Inactive tokens' requests are retained for audit trail.

## Request Search (Updated 2026-05-07)
Dashboard search box filters requests by:
- Method (exact match): "GET", "POST", etc.
- Path (substring): "/api/users"
- IpAddress (substring): "192.168" (supports IPv6)
- Minimum length: 2 characters to prevent broad scans

## Token Cache
`IMemoryCache` key `"token:{guid}"` — 5-minute sliding expiration.
`GetByTokenIncludingInactiveAsync` retrieves both active and inactive tokens (used by receiver path).
Invalidated (`cache.Remove`) on update, delete, set/reset custom-response.
Null results are never cached.

## Webhook Receiver Path
- Active token: persists request, notifies SSE subscribers, returns 200 (or custom response)
- Inactive token: persists request for audit trail, returns 410 Gone (signals sender to stop)
