# Documentation Update Summary — 2026-05-08

All documentation has been synchronized with the 2026-05-08 session changes.

## Files Updated

### 1. README.md

**§8 Solution Structure**
- `docker-compose.override.yml` comment: updated from `BaseUrl for :8088` → `BaseUrl from ${WEBHOOK_BASE_URL}` to reflect the actual file state

**§9 Docker Compose — Development Override**
- YAML snippet: `Webhook__BaseUrl: "http://localhost:8088"` → `"${WEBHOOK_BASE_URL}"` (reads from `.env`)
- Added note: changing `WEBHOOK_BASE_URL` in `.env` requires `docker compose up -d --force-recreate api` to take effect

### 2. docs/CODEMAPS/dependencies.md

**Tools section**
- Corrected stale `HashGen` reference — tool was never renamed; it is still `tools/RotatePassword`
- Added `--update-env` and interactive usage modes

### 3. docs/CODEMAPS/backend.md

**Options section**
- Updated `Webhook:BaseUrl` description to document that the `appsettings.json` default is `""` (empty, so validator fires when `WEBHOOK_BASE_URL` is unset)
- Added `appsettings.Development.json` default: `http://localhost:8080`

**New: WebhookUrl Computation section**
- Documents that `webhookUrl` is NOT stored in DB — computed at read time in `WebhookTokenExtensions.ToDto(baseUrl)`
- Notes that `GetTokenQueryHandler`, `GetTokensQueryHandler`, and `CreateTokenCommandHandler` all uniformly use `IOptions<WebhookOptions>`

### 4. docs/CODEMAPS/architecture.md

**New: Deployment Notes section**
- Documents `WEBHOOK_BASE_URL` precedence order (override.yml > docker-compose.yml > appsettings.Development > appsettings)
- Warns about the `docker-compose.override.yml` bug (fixed 2026-05-08)

### 5. docs/CODEMAPS/INDEX.md

- Updated Last Updated date to 2026-05-08
- Added 2026-05-08 Recent Changes block (kept 2026-05-07 history below)

## Source of Truth Verification

| Source File | Generated Section | Status |
|-------------|------------------|--------|
| `docker-compose.override.yml` | README §9 YAML snippet | Updated |
| `.env.example` | README §10 env vars table | Current (no new vars added) |
| `tools/RotatePassword/` | dependencies.md Tools section | Fixed (wrong name `HashGen`) |
| `GetTokenQueryHandler.cs` | backend.md Options + CQRS map | Updated |

## Staleness Check

All documentation files modified in the last 3 days — no staleness issues.

| File | Last Modified |
|------|--------------|
| README.md | 2026-05-08 |
| CLAUDE.md | 2026-05-07 |
| docs/CODEMAPS/architecture.md | 2026-05-08 |
| docs/CODEMAPS/backend.md | 2026-05-08 |
| docs/CODEMAPS/frontend.md | 2026-05-07 |
| docs/CODEMAPS/data.md | 2026-05-07 |
| docs/CODEMAPS/dependencies.md | 2026-05-08 |
| docs/CODEMAPS/INDEX.md | 2026-05-08 |

## Summary

```
Documentation Update — 2026-05-08
──────────────────────────────────────────────────────
Updated:  README.md §8 (override.yml comment)
Updated:  README.md §9 (YAML snippet + force-recreate note)
Updated:  docs/CODEMAPS/backend.md (Options, WebhookUrl Computation)
Updated:  docs/CODEMAPS/architecture.md (Deployment Notes)
Updated:  docs/CODEMAPS/dependencies.md (RotatePassword name fix)
Updated:  docs/CODEMAPS/INDEX.md (Last Updated + Recent Changes)
Skipped:  README.md §10 (env vars table current — no new variables)
Skipped:  frontend.md, data.md (no frontend/schema changes this session)
Flagged:  (none — all docs < 3 days old)
──────────────────────────────────────────────────────
```

## Generated

**Date**: 2026-05-08
**Generator**: /everything-claude-code:update-docs
**Status**: Complete
