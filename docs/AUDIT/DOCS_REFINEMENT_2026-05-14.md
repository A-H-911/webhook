# Documentation Refinement & Audit — 2026-05-14

**Date:** 2026-05-14
**Branch:** `main` (clean working tree at start of audit)
**Methodology:** Code-anchored doc audit. Three parallel read-only scans of the repo (documentation inventory, implementation ground truth, decision-trail/commits) cross-referenced against `README.md`, `CLAUDE.md`, and `docs/**/*.md`. Findings only listed where doc claims contradict, omit, or trail the current source-of-truth files.
**Driving plan:** `C:\Users\ahammo\.claude\plans\based-on-readme-md-and-snazzy-zebra.md`

## Scope

Doc surfaces audited and edited in this pass:

- `README.md` (1338 lines — focus on §5 HLD, §6 flows, §7 LLD, §8 structure, §11 security, §13 testing, §16 decisions, §17 trade-offs, §18 roadmap)
- `CLAUDE.md` (365 lines — Key Non-Obvious Facts, DANGER ZONE invariants)
- `docs/CODEMAPS/{INDEX,architecture,backend,data,frontend,dependencies}.md`
- `docs/RUNBOOK.md`, `docs/ENV.md`, `docs/CONTRIBUTING.md`

Out of scope: source-code changes; the unrelated `new_ui_ux_design.html`; ADR-style decision-log conventions.

## Drivers (what the docs failed to absorb)

Between v6 (2026-05-05) and 2026-05-14 three architectural pivots landed but only partially propagated into docs:

| # | Pivot | Anchor commits | Where docs lag |
|---|-------|----------------|----------------|
| D1 | **Three-process split** — API + StreamWorker + JobsWorker share Domain/Application/Infrastructure libraries with four focused DI extensions (`AddCoreInfrastructure` + `AddApiInfrastructure` / `AddStreamWorkerInfrastructure` / `AddJobsWorkerInfrastructure`). | `467db0b`, `56d11b0`, `5cbd93c` | README §16 missing; §5/§8 mention but no design rationale. |
| D2 | **Redis as first-class runtime** — `RedisTokenCache`, `RedisSseBridgeService`, `RedisSessionRevocationStore`, `RedisStreamConsumerService`, `RedisStreamPublisher`. | `3d26324`, `1cbf8b2` | README §7.7 still describes `IMemoryCache`; §17 trade-off and §18 roadmap describe Redis as future work. |
| D3 | **Hard-delete on token deletion** — `DeleteTokenCommand` is `repository.DeleteAsync` + EF cascade, not `token.Deactivate()`. | `5b693a3`, `e2bfee9`, `bba3927` | §8 caption says "soft-delete + hard-delete"; §17 trade-off says soft-deleted tokens accumulate. |

## Findings

Severity legend: **P0** = doc directly contradicts code · **P1** = significant gap or missing decision · **P2** = consistency / new artifact.

Status legend: **open** · **closed** · **deferred** (out of scope or for a later pass).

### P0 — Direct contradictions

| # | Finding | Evidence | Resolution | Status |
|---|---------|----------|------------|--------|
| P0-1 | Token cache documented as `IMemoryCache`; real implementation is `RedisTokenCache`. | `src/Hookbin.Infrastructure/Redis/RedisTokenCache.cs:9-67` (key prefix `wh:token:`, sliding TTL via `KeyExpireAsync`, fail-open on `RedisException`/`RedisTimeoutException`). All four mutation handlers call `ITokenCache.RemoveAsync` (e.g. `SetCustomResponseCommandHandler.cs:28`). Stale doc claims: `README.md:579,585,615,660`; CLAUDE.md "Token cache" row in Key Non-Obvious Facts; DANGER ZONE row "`cache.Remove(tokenId)` on every token mutation". | Rewrite README §7.7 around `ITokenCache` + `RedisTokenCache`, fix §8 tree captions on lines 615 + 660, replace CLAUDE.md cache claims. | closed |
| P0-2 | §8 tree caption claims `DeleteToken/ ← soft-delete + hard-delete`. Real implementation: hard-delete only. | `DeleteTokenCommandHandler.cs` calls `repository.DeleteAsync(id)`; `WebhookRequestConfiguration.cs:40` (`OnDelete(DeleteBehavior.Cascade)`). | Rewrite caption to "hard-delete + EF cascade + ITokenCache.Remove + SSE notify". | closed |
| P0-3 | §17 trade-off "Soft-deleted tokens remain in DB" is false for the delete path after the hard-delete transition. | See D3 above. | Replace with audit-trail-on-deactivate framing. | closed |
| P0-4 | §17 trade-off "Single API instance — SSE channels are in-process" is obsoleted by `RedisSseBridgeService` (Pub/Sub pattern `sse:*`). | `RedisSseBridgeService.cs`; registered via `AddApiInfrastructure()`. | Rewrite to describe two-stage fan-out (Pub/Sub → in-process bounded channels). | closed |
| P0-5 | §18 Future Roadmap row "Redis for SSE + distributed cache" describes work that has already shipped. | Same source as P0-1 and P0-4. | Remove row; record in §16.2 Revision History as "v7 — Redis integration". | closed |
| P0-6 | §18 Future Roadmap row "Hangfire" describes work explicitly rejected in §16.1 row 10. | `JobsWorker/Program.cs` + `RetentionCleanupService.cs` use `BackgroundService` + `PeriodicTimer(24h)`. | Remove row. | closed |

### P1 — Missing decisions / significant gaps

| # | Finding | Evidence | Resolution | Status |
|---|---------|----------|------------|--------|
| P1-1 | §16 has no entry for three-process architecture. | D1 commits; four DI extensions in `Hookbin.Infrastructure/DependencyInjection.cs`. | Add §16 decision + §17 single-replica trade-off. | closed |
| P1-2 | §16 has no entry for Redis Streams persistence backbone. | `RedisStreamPublisher.cs`, `RedisStreamConsumerService.cs` (consumer group `webhook-api`, consumer name from `HOOKBIN_WORKER_ID`, PEL recovery via `XREADGROUP "0-0"`, XACK + drop on FK violation). | Add §16 decision + §17 `HOOKBIN_WORKER_ID` stability trade-off. | closed |
| P1-3 | §16 has no entry for hard-delete + EF cascade rationale. | D3 commits; CLAUDE.md DANGER ZONE row. | Add §16 decision tying back to dashboard-metric correctness. | closed |
| P1-4 | §16 has no entry for Redis Pub/Sub SSE bridge. | `RedisSseBridgeService.cs`. | Add §16 decision; tighten §7.4 SSE Architecture. | closed |
| P1-5 | §16 has no entry for Redis-backed session revocation (cross-instance logout). | `RedisSessionRevocationStore.cs`; `Program.cs:76-86` `OnValidatePrincipal`. | Add §16 decision + §11 Security subsection. | closed |
| P1-6 | §11 Security model omits per-token rate limiting (250/sec), login brute-force limit (5/min), and antiforgery `X-XSRF-TOKEN` cookie emission. | `Program.cs:194` `UseRateLimiter`, `Program.cs:100-106` login policy, `Program.cs:131` `AddAntiforgery`, `Program.cs:199-209` cookie-emit middleware. | Extend §11 with three subsections (rate limiting, antiforgery flow, session revocation). | closed |
| P1-7 | §16 has no entry for ArchitectureTests as a CI gate. | `tests/Hookbin.ArchitectureTests/Layers/LayerDependencyTests.cs` and siblings; `.github/workflows/ci.yml` job `architecture-test`. | Add §16 decision; list ArchitectureTests as a fourth tier in §13 Testing. | closed |
| P1-8 | §16 has no entry for GeoIP enrichment via MaxMind (`IpCountry` field). | Migration `20260511160402_AddTokenNameAndRequestResponseAndCountry`; `WebhookRequest.IpCountry`; `WebhookController.Receive` line 37 calls `IGeoIpService.GetCountry`. | Add §16 decision. Note: enrichment runs on the **API hot path** in `WebhookController` before XADD (corrected during the devil's-advocate review — see follow-up section), not in the StreamWorker. | closed |
| P1-9 | §7.1 LLD entity table for `WebhookRequest` omits four fields added via migrations: `ProcessingTimeMs`, `Note`, `IpCountry`, `ResponseStatusCode`. | Migrations `20260510104619`, `20260510104653`, `20260511160402`; `src/Hookbin.Domain/Entities/WebhookRequest.cs`. | Refresh §7.1 table; document `SetRequestNoteCommand` in §7.5 CQRS map; reflect `Note`/`ProcessingTimeMs` in §7.2 DTOs. | closed |
| P1-10 | §17 missing three Redis-era trade-offs: (a) Redis runtime dependency with fail-open cache/session and fail-loud streams, (b) `HOOKBIN_WORKER_ID` stability, (c) single-replica JobsWorker without leader election. | CLAUDE.md DANGER ZONE rows already capture them as invariants. | Add three new §17 rows with "Acceptable Because" reasoning. | closed |
| P1-11 | §18 Future Roadmap is thin and stale. | See P0-5/P0-6; surfaced gaps include HA JobsWorker via leader election, OpenTelemetry traces, automatic PEL orphan reclaim, PII/secret redaction toggle, bulk export (NDJSON/ZIP). | Replace stale rows; keep "Multi-user / RBAC", "Full-Text Search", "Bulk export". | closed |
| P1-12 | §5.2 stack table omits Redis as a first-class component. | `docker-compose.yml` redis service; `src/Hookbin.Infrastructure/Redis/*`. | Add Redis 7 row; update §5.1 system context to include the Redis box and the three-process arrows. | closed |

### P2 — Refinement, consistency, new artifacts

| # | Finding | Resolution | Status |
|---|---------|------------|--------|
| P2-1 | No durable audit log for this refinement pass. | This file. | closed (this artifact is the resolution) |
| P2-2 | CLAUDE.md "Key Non-Obvious Facts" needs P0-1, P1-5, P1-6 reflected. | Edit cache row; add session revocation, rate limiting, antiforgery rows. | closed |
| P2-3 | CLAUDE.md DANGER ZONE should add Redis fail-open semantics row; verify `RedisSseBridgeService` invariant is current. | Append rows; verify existing rows. | closed |
| P2-4 | Codemaps were refreshed 2026-05-14; spot-check for any surviving `IMemoryCache` / soft-delete framing. | Read-and-correct pass. | closed |
| P2-5 | RUNBOOK Redis operational procedures + single-replica/HOOKBIN_WORKER_ID deployment rule. | Add/correct subsections. | closed |
| P2-6 | ENV coverage of `ConnectionStrings__Redis`, `HOOKBIN_WORKER_ID`, `Auth__SessionHours`, rate-limit knobs, BCrypt rotation. | Add missing rows. | closed |
| P2-7 | CONTRIBUTING "Add a new CQRS command" recipe alignment with four DI extensions + architecture tests. | Read-and-correct pass. | closed |
| P2-8 | §16.2 Revision History needs a v7 row for this refinement. | Append row. | closed |
| P2-9 | Optional: add §6 Flow F — Login & session revocation. | Defer if §11 Security covers the flow narratively. | deferred |

## Cross-File Impact Map

| Primary edit | Forced downstream check |
|--------------|-------------------------|
| §7.7 token cache (P0-1) | CLAUDE.md cache row; codemap `architecture.md` + `backend.md`; RUNBOOK; ENV (`ConnectionStrings__Redis`). |
| §8 tree captions (P0-1, P0-2) | codemap `backend.md`; CONTRIBUTING "Add a new CQRS command" recipe. |
| §16 new decisions (P1-1 → P1-8) | §5.2 stack table; §17; CLAUDE.md DANGER ZONE; codemap `architecture.md` + `data.md`; RUNBOOK. |
| §17 rewrites (P0-3, P0-4, P1-10) | §18 roadmap; CLAUDE.md DANGER ZONE. |
| §18 rewrite (P0-5, P0-6, P1-11) | §16.2 Revision History (P2-8). |
| §7.1 entity fields (P1-9) | §7.2 DTOs; §7.5 CQRS map; codemap `data.md`. |
| §11 security additions (P1-5, P1-6) | CLAUDE.md DANGER ZONE; RUNBOOK; ENV. |

## Verification Checklist (closed at end of Phase 4)

1. `Grep "IMemoryCache" README.md CLAUDE.md docs/` → zero hits except an intentional historical note.
2. `Grep -n "soft-delete" README.md CLAUDE.md docs/` → only deactivate-path mentions.
3. §18 contains no rows for Redis SSE, Redis cache, or Hangfire.
4. §16 decision count ≥ 30 (was 22).
5. §17 trade-off count adjusted (two corrected + three new).
6. §7.1 `WebhookRequest` table lists `ProcessingTimeMs`, `Note`, `IpCountry`, `ResponseStatusCode`.
7. CLAUDE.md DANGER ZONE — every row matches code or a README §16/§17 row.
8. `dotnet build` succeeds; `dotnet test tests/Hookbin.UnitTests/` passes (defense-in-depth; no code changes).
9. Every finding in this file marked **closed** with commit/file reference by end of Phase 3.
10. Frontend dev-stack smoke check (manual or deferred to next session).

## Follow-up issues (out of scope here, logged for next pass)

- §6 Flow F (login + session revocation lifecycle) — leave for a focused auth-docs pass once §11 absorbs P1-5/P1-6 narratively.
- ADR-style decision log seeded under `docs/ADR/` — owner-time call; this artifact provides the same durable record without a new convention.
- Mutation-score follow-up from `BASELINE.md` (Stryker against integration suite) — orthogonal initiative.

## Resolution Summary

**Closed:** 26 findings — P0-1…P0-6, P1-1…P1-12, P2-1…P2-8.
**Deferred:** 1 finding — P2-9 (§6 Flow F).

### Phase outcomes

- **Phase 1 (P0 corrections):** Cache abstraction misclaim (`IMemoryCache` → `RedisTokenCache`) corrected at every stale call-site (`README.md` §7.7, §8 captions, Flow C, Flow D, §7.5 CQRS map; `CLAUDE.md` Key Non-Obvious Facts, DANGER ZONE, feature recipe). Hard-delete framing corrected on §7.3 API Contract row, §7.1 IsActive description, §7.5 row, §8 caption, Flow D narrative. Stale `§17` trade-offs rewritten and stale `§18` roadmap items removed (Redis-future, Hangfire). `dotnet test tests/Hookbin.UnitTests/` passes 377/377 — confirms doc-only edits.
- **Phase 2 (P1 enrichment):** §16.1 grew from 22 → 30 confirmed decisions (rows #23 three-process, #24 Redis Streams, #25 hard-delete + EF cascade, #26 Redis Pub/Sub SSE bridge, #27 session revocation, #28 rate limiting + antiforgery, #29 ArchUnit/NetArchTest CI gate, #30 GeoIP enrichment; rows #10, #11, #15, #17, #18 reframed in place). §16.2 v7 row added. §17 picked up three new trade-offs (Redis runtime dependency, `HOOKBIN_WORKER_ID` stability, single-replica JobsWorker). §18 refreshed (HA JobsWorker, PEL orphan reclaim, OpenTelemetry, PII redaction toggle, signed-URL forwarding). §7.1 entity table picked up `IpCountry` + `ResponseStatusCode`; §7.4 SSE Architecture extended with the Redis Pub/Sub bridge diagram. §11 Security gained subsections for antiforgery, session revocation, and rate limiting. §13 Testing gained an Architecture Tests tier. CLAUDE.md DANGER ZONE picked up Redis fail-open semantics, antiforgery, rate-limiter, and `ITokenCache` invariants; Key Non-Obvious Facts picked up session revocation, rate limiting, antiforgery, GeoIP. `dotnet test tests/Hookbin.UnitTests/` still passes 377/377.
- **Phase 3 (Cross-doc sweep):** Three real stale claims corrected: `docs/RUNBOOK.md:646` (Memory Cache Tuning → Token Cache Tuning with Redis details), `docs/ENV.md` Redis subsection (rewrote false "only required if running StreamWorker" + "API uses IMemoryCache" claims), `docs/CODEMAPS/INDEX.md:173` (`RedisTokenCache` no longer described as wrapping `IMemoryCache`). Other stale-signal hits in `docs/` are intentional (this audit artifact itself, historical migration script, codemap descriptions of the soft→hard-delete transition).
- **Phase 4 (Verification):** §16.1 decision count = 30 confirmed (verification target ≥ 30 met). No surviving `IMemoryCache` claim except the DANGER ZONE invariant (intentional) and §16.3 row B2 (historical record of original fix). No surviving Hangfire reference except the negation rows ("No Hangfire", "Hangfire removed v3"). All P0 + P1 + P2 (except deferred P2-9) findings closed.

### Pending follow-ups

- P2-9 (§6 Flow F login lifecycle diagram) — left for a focused auth-docs pass once the new §11 subsections settle.
- Frontend dev-stack smoke check — deferred to a manual session (no code changed, so behavior should be unchanged).

## Devil's-Advocate Review (second pass, same day)

A follow-up adversarial review of the Phase 1–3 edits surfaced **9 additional errors**: 6 introduced by trusting secondhand agent summaries without opening the actual source, and 3 pre-existing inaccuracies in §7.4 that the primary pass edited *around* without correcting. All 9 are now fixed.

### Errors introduced in the primary pass

| # | Where | Original (wrong) claim | Verified reality | Source of truth |
|---|---|---|---|---|
| E1 | README §11 | `RedisSessionRevocationStore` is in `src/Hookbin.Infrastructure/Redis/` | Lives in `src/Hookbin.API/Services/RedisSessionRevocationStore.cs` alongside `ISessionRevocationStore`. The token cache + SSE bridge are in Infrastructure/Redis/; the revocation store is **not**. | File path verified by Read |
| E2 | README §11 rate-limiter table | "Per-IP login \| 5 req/min \| Real client IP from `X-Forwarded-For`" | `AddFixedWindowLimiter("login", …)` is a **global** named policy with no partition. All login attempts share one 5/min bucket worldwide. The single-admin trust model justifies the strictness. | `Program.cs:100-106` |
| E3 | CLAUDE.md DANGER ZONE | "Both [limiters] depend on `UseForwardedHeaders` resolving the real client IP first" | Neither limiter uses the client IP. The receiver partitions by route value `{token:guid}` (`Program.cs:111-121`); the login policy is global. The middleware ordering is correct for other reasons. | `Program.cs:98-124` |
| E4 | README §7.4 | "The 10-per-token cap is enforced by a Redis counter (`wh:sse-count:{tokenId}`) incremented via a Lua script in `SseController` before the SSE loop starts" | Lua script is defined and run inside `SseNotifier.TrySubscribeAsync` (`SseNotifier.cs:14-48`). The controller only *calls* `TrySubscribeAsync` and turns the returned `false` into 429. | `SseNotifier.cs:14-48` |
| E5 | README §16 row #30, §7.1 `IpCountry` row | "GeoIP enrichment populated by the StreamWorker during persistence via MaxMind" | Runs on the **API hot path** in `WebhookController.Receive` (`WebhookController.cs:37`) via `IGeoIpService.GetCountry` **before** the request is XADDed to the Redis stream. The country tag travels through the stream payload. | `WebhookController.cs:36-37` |
| E6 | This audit artifact's Phase 2 summary | Repeated the StreamWorker GeoIP claim | Same correction propagated. | — |

### Pre-existing inaccuracies in §7.4 left stale by the primary pass

| # | Where | Stale claim | Verified reality | Source of truth |
|---|---|---|---|---|
| E7 | README §7.4 SSE diagram | `Channel<SseEvent>(capacity: 50)` | `new BoundedChannelOptions(100)` — capacity is 100 with `FullMode = DropOldest`, `SingleReader = true`, `SingleWriter = false`. | `SseNotifier.cs:80-86` |
| E8 | README §7.4 SSE diagram | `ConcurrentDictionary<tokenId, ConcurrentDictionary<channelId, Channel<SseEvent>>>` (2-level) | `ConcurrentDictionary<Guid, List<Channel<SseEvent>>>` (1-level). Channels are tracked as a `List<>` per token, not by a per-channel id. | `SseNotifier.cs:29` |
| E9 | README §7.4 SSE diagram | "lock(perTokenLock): if count >= 10 → throw TooManyConnectionsException → 429" | One single global `Lock _lock` (not per-token) protects the dictionary; the 10-cap is enforced cross-instance by the Redis Lua script in `TrySubscribeAsync`, which **returns `false`** rather than throwing. The controller maps `false` to 429. | `SseNotifier.cs:30,32-48` |

### Lessons captured for future passes

1. **Never trust an exploration agent's summary for load-bearing claims.** The primary pass relied on a single agent's "ground truth" report for several precise file paths, method names, and behaviors. The agent was largely accurate but wrong on E1 (`RedisSessionRevocationStore` path), E2 (login per-IP), E4 (Lua script location), and E5 (GeoIP location). The fix is to **open every cited file** before writing a documentation claim that points at it with `file:line` precision.
2. **Edit-around-stale-content is a smell.** When the new edit is in the same section as a stale paragraph, fix the stale paragraph in the same pass — otherwise the section becomes internally inconsistent (here: existing line said 50, new line said 100, both in §7.4 within 30 lines of each other).
3. **The audit report itself needs an adversarial review.** A finding marked "closed" can still cite the wrong file path or invent a behavior the code doesn't have. Re-running the verification grep against the new content (not just the old content) would have caught E1 immediately.

### Final state after devil's-advocate fixes

- README.md, CLAUDE.md, and `docs/AUDIT/DOCS_REFINEMENT_2026-05-14.md` all corrected.
- Source-of-truth files used for verification: `src/Hookbin.Infrastructure/Sse/SseNotifier.cs`, `src/Hookbin.API/Services/RedisSessionRevocationStore.cs`, `src/Hookbin.API/Controllers/AuthController.cs`, `src/Hookbin.API/Controllers/WebhookController.cs`, `src/Hookbin.Infrastructure/Redis/RedisStreamConsumerService.cs`, `src/Hookbin.Infrastructure/Redis/RedisSseBridgeService.cs`, `src/Hookbin.API/Program.cs`.
- `dotnet test tests/Hookbin.UnitTests/` re-run after the corrections — still passes.
