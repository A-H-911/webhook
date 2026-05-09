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
- Added note: `RetentionCleanupService` runs in the `WebhookService.JobsWorker` process (not the API)
- Added batch size (5,000 rows/loop) and single-replica constraint

**§8 Solution Structure**
- Added `IRequestQueuePublisher.cs` (Domain/Services)
- Added `ITokenCache.cs` and `Caching/` directory (Application)
- Added full `Redis/` directory listing: `RedisStreamPublisher.cs`, `RedisTokenCache.cs`, `RedisStreamConsumerService.cs`, `RedisSseBridgeService.cs` (Infrastructure)
- Updated `DependencyInjection.cs` comment: four focused extensions
- Added `WebhookService.StreamWorker/` and `WebhookService.JobsWorker/` project sections
- Added `Services/` directory: `ISessionRevocationStore.cs`, `RedisSessionRevocationStore.cs` (API)

**§9 Docker Compose Services Table**
- Added `redis` (redis:7-alpine, internal), `stream-worker`, and `jobs-worker` rows
- Added notes about single-replica constraint for `jobs-worker` and `depends_on` ordering

**§10 Configuration Reference**
- Added `WEBHOOK_WORKER_ID` row — optional, stable Redis consumer identity for StreamWorker; prevents PEL orphaning
- Added `ConnectionStrings__Redis` row — required for stream-worker; Redis host:port
- Added `.env` quoting rule box: explains `$letter` interpolation trap and how to prevent it with single quotes

### 2. docs/CODEMAPS/ (all five files — committed separately as `31401bc`)

Already updated and committed in the `/update-codemaps` run earlier this session.

## Source of Truth Verification

| Source File | Generated Section | Status |
|-------------|------------------|--------|
| `docker-compose.yml` | README §9 services table | Updated |
| `.env.example` | README §10 env vars table | Updated (WEBHOOK_WORKER_ID, ConnectionStrings__Redis added) |
| `src/WebhookService.StreamWorker/Program.cs` | README §8 StreamWorker section | Updated |
| `src/WebhookService.JobsWorker/Program.cs` | README §8 JobsWorker section | Updated |
| `src/WebhookService.Infrastructure/DependencyInjection.cs` | README §8 DI extension comment | Updated |
| `src/WebhookService.Infrastructure/Redis/` | README §8 Redis/ directory listing | Updated |

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
Updated:  README.md §10 (WEBHOOK_WORKER_ID, ConnectionStrings__Redis, .env quoting rule)
Updated:  docs/CODEMAPS/architecture.md (three-process diagram, DI extension table)
Updated:  docs/CODEMAPS/backend.md (service interfaces, worker files)
Updated:  docs/CODEMAPS/dependencies.md (Redis services, WEBHOOK_WORKER_ID, curl)
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
