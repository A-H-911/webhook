# Documentation Update Summary — 2026-05-11

All documentation has been synchronized with the 2026-05-11 session changes (per-request notes, processing time, threat intelligence links, parsed query/form tables, test suite enhancements, rebuild scripts).

## Files Updated

### README.md

**§3 Features**
- Added: per-request notes (user-editable, PATCH endpoint)
- Added: processing time chip (StreamWorker latency, nullable until processed)
- Added: parsed display — query string and form body rendered as key-value tables
- Added: threat intelligence links from IP address (Whois / Shodan / VirusTotal / Censys)

**§7.1 Domain Entities — WebhookRequest**
- Added `ProcessingTimeMs BIGINT NULL` and `Note NVARCHAR(2000) NULL` columns to entity table
- Added covering index `TokenId + ReceivedAt + Id` to the indexes note

**§7.2 DTOs**
- `WebhookRequestDetailDto` now lists `ProcessingTimeMs (long?)` and `Note (string?)`

**§7.3 API Contract — Request Management**
- Added `PATCH /api/tokens/{tokenId}/requests/{id}/note` row

**§7.5 CQRS Map — Request Commands**
- Added `SetRequestNoteCommand` row
- Annotated `GetRequestByIdQuery` to mention `ProcessingTimeMs + Note` in its return shape

### docs/CODEMAPS/ (all five files)
Already updated in the `/update-codemaps` run this session.

### CLAUDE.md
Already updated in Phase 7 (testing quick-ref + rebuild script gotcha).

## Staleness Check

All documentation files touched in the 2026-05-10 or 2026-05-11 sessions — no files exceed the 90-day staleness threshold.

| File | Last Modified |
|------|--------------|
| README.md | 2026-05-11 |
| CLAUDE.md | 2026-05-11 |
| docs/CODEMAPS/backend.md | 2026-05-11 |
| docs/CODEMAPS/frontend.md | 2026-05-11 |
| docs/CODEMAPS/data.md | 2026-05-11 |
| docs/CODEMAPS/dependencies.md | 2026-05-11 |
| docs/CODEMAPS/INDEX.md | 2026-05-11 |

## Summary

```
Documentation Update — 2026-05-11
──────────────────────────────────────────────────────
Updated:  README.md §3  (per-request notes, processing time, threat links, parsed display)
Updated:  README.md §7.1 (ProcessingTimeMs + Note columns, covering index)
Updated:  README.md §7.2 (WebhookRequestDetailDto — ProcessingTimeMs + Note fields)
Updated:  README.md §7.3 (PATCH .../note endpoint row)
Updated:  README.md §7.5 (SetRequestNoteCommand row; GetRequestByIdQuery annotation)
Skipped:  README.md §13 (already updated in Phase 7 — rebuild script in step 3)
Skipped:  CLAUDE.md (already updated in Phase 7 — rebuild script quick-ref)
Flagged:  (none — all docs updated today)
──────────────────────────────────────────────────────
```

## Generated

**Date**: 2026-05-11
**Generator**: /everything-claude-code:update-docs
**Status**: Complete

---

# Documentation Update Summary — 2026-05-10

All documentation has been synchronized with the 2026-05-10 session changes (three-process architecture).

## Files Updated

### 1. README.md

**§5.1 System Context**
- ASCII diagram updated from 4-service to 7-service layout: added Redis stream, `StreamWorker`, `JobsWorker`
- Shows full pub/sub fan-out path: StreamWorker → `PUBLISH sse:{tokenId}` → API `RedisSseBridgeService` → SSE HTTP clients

**§5.2 Stack**
- Added `Messaging: Redis 7` row
- Updated Real-time description to mention `RedisSseBridgeService` cross-process SSE bridge

**§6 Flow A (Webhook Receive)**
- Steps 8–9 updated: `WebhookController` now XADD to Redis stream (not direct DB write); persistence happens async in StreamWorker; SSE delivered via pub/sub → RedisSseBridgeService

**§6 Flow E (Retention Cleanup)**
- Added note: `RetentionCleanupService` runs in the `Hookbin.JobsWorker` process (not the API)
- Added batch size (5,000 rows/loop) and single-replica constraint

**§8 Solution Structure**
- Added `IRequestQueuePublisher.cs` (Domain/Services)
- Added `ITokenCache.cs` and `Caching/` directory (Application)
- Added full `Redis/` directory listing: `RedisStreamPublisher.cs`, `RedisTokenCache.cs`, `RedisStreamConsumerService.cs`, `RedisSseBridgeService.cs` (Infrastructure)
- Updated `DependencyInjection.cs` comment: four focused extensions
- Added `Hookbin.StreamWorker/` and `Hookbin.JobsWorker/` project sections
- Added `Services/` directory: `ISessionRevocationStore.cs`, `RedisSessionRevocationStore.cs` (API)

**§9 Docker Compose Services Table**
- Added `redis` (redis:7-alpine, internal), `stream-worker`, and `jobs-worker` rows
- Added notes about single-replica constraint for `jobs-worker` and `depends_on` ordering

**§10 Configuration Reference**
- Added `HOOKBIN_WORKER_ID` row — optional, stable Redis consumer identity for StreamWorker; prevents PEL orphaning
- Added `ConnectionStrings__Redis` row — required for stream-worker; Redis host:port
- Added `.env` quoting rule box: explains `$letter` interpolation trap and how to prevent it with single quotes

### 2. docs/CODEMAPS/ (all five files — committed separately as `31401bc`)

Already updated and committed in the `/update-codemaps` run earlier this session.

## Source of Truth Verification

| Source File | Generated Section | Status |
|-------------|------------------|--------|
| `docker-compose.yml` | README §9 services table | Updated |
| `.env.example` | README §10 env vars table | Updated (HOOKBIN_WORKER_ID, ConnectionStrings__Redis added) |
| `src/Hookbin.StreamWorker/Program.cs` | README §8 StreamWorker section | Updated |
| `src/Hookbin.JobsWorker/Program.cs` | README §8 JobsWorker section | Updated |
| `src/Hookbin.Infrastructure/DependencyInjection.cs` | README §8 DI extension comment | Updated |
| `src/Hookbin.Infrastructure/Redis/` | README §8 Redis/ directory listing | Updated |

## Staleness Check

All documentation files modified today — no staleness issues.

| File | Last Modified |
|------|--------------|
| README.md | 2026-05-10 |
| CLAUDE.md | 2026-05-10 |
| docs/CODEMAPS/architecture.md | 2026-05-10 |
| docs/CODEMAPS/backend.md | 2026-05-10 |
| docs/CODEMAPS/frontend.md | 2026-05-07 |
| docs/CODEMAPS/data.md | 2026-05-10 |
| docs/CODEMAPS/dependencies.md | 2026-05-10 |
| docs/CODEMAPS/INDEX.md | 2026-05-10 |

## Summary

```
Documentation Update — 2026-05-10
──────────────────────────────────────────────────────
Updated:  README.md §5.1 (system context diagram — three-process)
Updated:  README.md §5.2 (stack table — Redis added)
Updated:  README.md §6 Flow A (XADD path, async SSE delivery)
Updated:  README.md §6 Flow E (JobsWorker process, batch size)
Updated:  README.md §8 (StreamWorker, JobsWorker, Redis/ infrastructure)
Updated:  README.md §9 (redis, stream-worker, jobs-worker service rows)
Updated:  README.md §10 (HOOKBIN_WORKER_ID, ConnectionStrings__Redis, .env quoting rule)
Updated:  docs/CODEMAPS/architecture.md (three-process diagram, DI extension table)
Updated:  docs/CODEMAPS/backend.md (service interfaces, worker files)
Updated:  docs/CODEMAPS/dependencies.md (Redis services, HOOKBIN_WORKER_ID, curl)
Updated:  docs/CODEMAPS/data.md (ITokenCache, Redis stream schema, retention process)
Updated:  docs/CODEMAPS/INDEX.md (date, Recent Changes 2026-05-10)
Skipped:  frontend.md (no frontend changes this session)
Flagged:  (none — all docs updated today)
──────────────────────────────────────────────────────
```

## Generated

**Date**: 2026-05-10
**Generator**: /everything-claude-code:update-docs
**Status**: Complete
