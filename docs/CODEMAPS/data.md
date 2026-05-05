<!-- Generated: 2026-05-05 | Files scanned: 5 | Token estimate: ~350 -->

# Data Architecture

## Database
SQL Server 2022 (Docker) — single database `WebhookDb`

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
ReceivedAt      DATETIMEOFFSET    NOT NULL  INDEX
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

INDEX IX_WebhookRequests_TokenId
INDEX IX_WebhookRequests_ReceivedAt
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

## Retention
`RetentionCleanupService` deletes requests older than `Webhook:RetentionDays` on a 24-hour `PeriodicTimer`

## Token Cache
`IMemoryCache` key `"token:{guid}"` — 5-minute sliding expiration.
Invalidated (`cache.Remove`) on update, delete, set/reset custom-response.
Null results are never cached.
