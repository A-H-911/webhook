# Contributing Guide

<!-- AUTO-GENERATED:HEADER START -->
**Last Updated:** 2026-05-14 (test counts refreshed: integration 85→89, E2E 64→66, total 788; hard-delete on `DeleteTokenCommand` (EF cascade) and StreamWorker FK-violation ACK + drop landed since prior generation — see `docs/CODEMAPS/INDEX.md`)
<!-- AUTO-GENERATED:HEADER END -->

This guide explains how to set up the development environment, run tests, and submit pull requests to the Hookbin project.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Local Development Setup](#local-development-setup)
3. [Backend Development](#backend-development)
4. [Frontend Development](#frontend-development)
5. [Testing](#testing)
6. [Git Workflow](#git-workflow)
7. [Code Style & Review](#code-style--review)
8. [Pull Request Checklist](#pull-request-checklist)

---

## Prerequisites

### All Contributors

- Git
- Editor: VS Code (recommended), Visual Studio, or equivalent
- Docker Desktop (for integration tests and full-stack testing)

### Backend

- **.NET 10 SDK** — [install here](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (for integration tests via Testcontainers)

### Frontend

- **Node.js 20+** — [install here](https://nodejs.org/)
- npm comes with Node.js

---

## Local Development Setup

### 1. Clone and initialize

```bash
git clone <repo-url>
cd webhook
cp .env.example .env
```

### 2. Configure .env for local development

Edit `.env`:

```env
SA_PASSWORD=Dev@12345!
HOOKBIN_BASE_URL=http://localhost:8088
RETENTION_DAYS=7
MAX_REQUEST_SIZE_MB=5
AUTH_USERNAME=admin
AUTH_PASSWORD_HASH=<generate with tools/RotatePassword>
AUTH_SESSION_HOURS=8
```

To generate a BCrypt hash:

```bash
dotnet run --project tools/RotatePassword -- --password "YourTestPassword123!"
```

Then paste the generated hash into `AUTH_PASSWORD_HASH` in `.env`.

### 3. Docker Desktop check

Ensure Docker Desktop is running:

```bash
docker --version
docker compose version
```

Both must report v2+ for compose.

---

## Backend Development

### Build

```bash
dotnet build
```

### Format Code

```bash
dotnet format
```

Reformats all `.cs` files to project style. Run this before committing.

### Run Architecture Tests (no Docker required)

```bash
dotnet test tests/Hookbin.ArchitectureTests/
```

Or use the cross-OS scripts:

```bash
# PowerShell 7+ (Windows / Linux / macOS)
pwsh scripts/run-arch-tests.ps1

# Bash (Linux / macOS / Git Bash on Windows)
bash scripts/run-arch-tests.sh
```

47 rules verified by assembly reflection — layer dependencies, CQRS naming conventions, entity immutability, repository placement, folder-to-namespace alignment, plus **DANGER ZONE structural guards** (private-set `[JsonInclude]`, repository `.AsNoTracking`, worker `MigrateAsync` exclusion, `[AllowAnonymous]` on webhook + health) and **operational snapshots** (docker-compose `jobs-worker.deploy.replicas: 1`, `stream-worker.HOOKBIN_WORKER_ID` stable, nginx SSE `proxy_buffering off`, worker `.csproj` excludes `EFCore.Design`). Takes ~1 second. **No Docker required.** Run this before pushing any structural change.

### Run Unit Tests (no Docker required)

```bash
dotnet test tests/Hookbin.UnitTests/
```

Fast feedback loop — domain logic, CQRS handlers, in-memory services. ~377 tests, takes ~10 seconds.

### Run Integration Tests (Docker required)

```bash
dotnet test tests/Hookbin.IntegrationTests/
```

Testcontainers pulls SQL Server 2022 and Redis 7 containers automatically. Ensure Docker is running. ~89 tests, takes ~90 seconds.

### Run Mutation Tests (optional, deeper feedback)

```bash
dotnet tool install -g dotnet-stryker            # one-time
dotnet stryker --project src/Hookbin.Domain/Hookbin.Domain.csproj --solution Hookbin.slnx --reporter html
dotnet stryker --project src/Hookbin.Application/Hookbin.Application.csproj --solution Hookbin.slnx --reporter html
```

Mutation score gate: **≥ 60%** on `Hookbin.Domain` and `Hookbin.Application` (current 86% / 90%). HTML reports land in `StrykerOutput/<timestamp>/reports/mutation-report.html`. `Hookbin.Infrastructure` mutation score (~53%) is intentionally below the gate because most infrastructure code is exercised via integration tests rather than unit tests — see `docs/AUDIT/BASELINE.md`.

### Run All Backend Tests with Coverage

```bash
dotnet test --settings tests/coverlet.runsettings
```

Enforces 80% line/method coverage. Reports are generated in `TestResults/`.

### Run a Single Test by Name

```bash
dotnet test --filter "FullyQualifiedName~TestMethodName"
```

Example:

```bash
dotnet test --filter "FullyQualifiedName~PostToWebhook_Returns200_AndCreatesRequest"
```

### Apply EF Core Migrations

When working on database changes:

```bash
dotnet ef migrations add <DescriptiveName> \
  --project src/Hookbin.Infrastructure \
  --startup-project src/Hookbin.API
```

Review the generated migration file. Destructive changes (drops, renames) require manual safety review before committing.

Apply to local database:

```bash
dotnet ef database update \
  --project src/Hookbin.Infrastructure \
  --startup-project src/Hookbin.API
```

### Run the API Locally

For local development without Docker:

```bash
# Start a temporary SQL Server container (if not already running)
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=Dev@12345!" \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

# Build and run the API
dotnet build
dotnet run --project src/Hookbin.API
```

API listens on `http://localhost:8080`. Configure `appsettings.Development.json` or set `WEBHOOK__BaseUrl`:

```bash
export WEBHOOK__BaseUrl=http://localhost:8080
dotnet run --project src/Hookbin.API
```

---

## Frontend Development

### Install Dependencies

```bash
cd frontend/hookbin-spa
npm install
```

### Start Dev Server

```bash
npm start
```

Runs on `http://localhost:4200`. Proxies `/api`, `/webhook`, `/health` to `http://localhost:8080` (the .NET API must be running).

### Production Build

```bash
npm run build
```

Output is in `dist/`.

### Run Unit Tests

```bash
npm test
```

Runs Vitest. ~209 tests, covers components, services, pipes, modal/toast infra (custom CDK Overlay — Angular Material is fully removed), and DANGER ZONE invariants (interceptor `/api/auth/*` exclusion, SSE `withCredentials`, SSE reconnect).

### Coverage Report

```bash
npm test -- --watch=false --coverage
```

Generates HTML report in `coverage/`. Coverage targets: 80% stmt, 75% branch, 80% function, 80% line.

### Format Code

The project uses Prettier. Format before commit:

```bash
npx prettier --write "src/**/*.ts" "src/**/*.html"
```

Or configure your editor to format on save.

---

## Testing

### Testing Strategy

The project uses a **5-tier test pyramid**:

<!-- AUTO-GENERATED:TEST-COUNTS START -->
1. **Architecture Tests** (47 rules) — Assembly reflection + file content snapshots; no infrastructure; fastest (~1s)
2. **Unit Tests** (377 backend + 209 frontend) — Fast, isolated, no infrastructure (~15s combined)
3. **Integration Tests** (89 backend) — Real SQL Server + Redis containers via Testcontainers; WebApplicationFactory (~90s)
4. **E2E Tests** (66 via Playwright) — Full stack running; user workflows end-to-end (~60s with shared fixture)
5. **Mutation Tests** (optional, Stryker.NET) — Falsifiable pin verification; gate ≥60% on Domain + Application

All non-mutation tests must pass before submitting a PR. Mutation tests are advisory but recommended when changing CQRS handler logic, domain invariants, or serialization contracts.

**Total tests: 788 (architecture + unit + integration + E2E + frontend), all green at 2026-05-14 (CI run 25829442444).**
<!-- AUTO-GENERATED:TEST-COUNTS END -->

### Architecture Tests (Fastest — 1 second)

```bash
dotnet test tests/Hookbin.ArchitectureTests/
```

**When architecture tests fail:**
- You added a type to the wrong namespace (e.g., a handler in `API` instead of `Application`)
- A command is a `class` instead of a `sealed record`
- A handler is `public` instead of `internal sealed`
- A source file's folder path doesn't match its CLR namespace
- `Hookbin.Domain` accidentally references an outer layer

**Intentionally breaking a rule?** Update the corresponding test file in `tests/Hookbin.ArchitectureTests/` in the same PR. The test failure is the early-warning system.

### Unit Tests (Fast — 10 seconds)

```bash
# Backend
dotnet test tests/Hookbin.UnitTests/

# Frontend
cd frontend/hookbin-spa && npm test
```

**When to add unit tests:**
- New domain entity invariants or mutation methods
- CQRS command/query handler logic
- Service utilities
- Component creation and template assertions

### Integration Tests (Medium — 60 seconds)

```bash
dotnet test tests/Hookbin.IntegrationTests/
```

**When to add integration tests:**
- New API endpoints
- Database queries or persistence logic
- Redis stream interactions
- Health check behavior

**Note:** Requires Docker to be running. Testcontainers manages SQL Server lifecycle automatically.

### E2E Tests (Slow — 3 minutes)

```bash
# Step 1: Build the solution (generates Playwright runtime)
dotnet build

# Step 2: Install Playwright browsers (first run only)
pwsh tests/Hookbin.E2ETests/bin/Debug/net10.0/playwright.ps1 install

# Step 3: Start the full stack
docker compose up -d

# Step 4: Run E2E tests
E2E_BASE_URL=http://localhost:8088 E2E_AUTH_PASSWORD=Admin123! dotnet test tests/Hookbin.E2ETests/
```

Or use the rebuild script (recommended):

```bash
pwsh scripts/rebuild-and-wait.ps1
```

Then run tests:

```bash
E2E_BASE_URL=http://localhost:8088 E2E_AUTH_PASSWORD=Admin123! dotnet test tests/Hookbin.E2ETests/
```

**When to add E2E tests:**
- Critical user workflows (login, create token, view requests, search, delete)
- Cross-service interactions (API → frontend → SSE)
- Custom response and retention behavior

### Continuous Integration (CI)

All checks run automatically on push and pull request:

```bash
# GitHub Actions workflow — .github/workflows/ci.yml

build-and-test     (parallel) — dotnet test tests/Hookbin.UnitTests/ + build
architecture-test  (parallel) — dotnet test tests/Hookbin.ArchitectureTests/ [<60s, no Docker]
frontend           (parallel) — npm run build + npm test --coverage
integration-test   (after build-and-test) — dotnet test tests/Hookbin.IntegrationTests/
e2e-test           (after build-and-test + frontend) — full stack + Playwright
```

All must pass before merge. Architecture tests run in parallel with unit tests — they'll catch structural violations before the slower integration/E2E jobs even start.

---

## Git Workflow

### Branch Naming

- Feature: `feat/<short-description>` — e.g., `feat/custom-response-headers`
- Bug fix: `fix/<short-description>` — e.g., `fix/sse-connection-limit`
- Docs: `docs/<short-description>` — e.g., `docs/add-troubleshooting-guide`
- Refactor: `refactor/<short-description>` — e.g., `refactor/extract-token-cache`
- Test: `test/<short-description>` — e.g., `test/add-retention-coverage`

### Commit Format

```
<type>: <description>

<optional body explaining why and how>
```

**Types:** `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `ci`

**Examples:**

```
feat: add per-request notes with PATCH endpoint

Allows users to annotate captured requests with freetext notes up to 2000 chars.
Implemented as a new CQRS command; notes are persisted in WebhookRequests.Note column.

fix: guard SSE response writes with HasStarted check

Prevents InvalidOperationException when writing to an already-started SSE response.

test: add branch coverage for retention cleanup error handling
```

### Creating a PR

1. Push your branch
2. Open a pull request to `main`
3. Complete the checklist below
4. Request review

---

## Code Style & Review

### C# Style

- **Naming:** `camelCase` for locals/params, `PascalCase` for types/public members
- **Immutability:** Prefer creating new objects; avoid mutations
- **Error handling:** Explicit `try/catch`; never silently swallow exceptions
- **CQRS:** All writes go through `MediatR` commands; queries through queries
- **Repository pattern:** Encapsulate data access; depend on interfaces, not EF Core directly
- **DI:** Constructor injection only; no `ServiceLocator` pattern

Run `dotnet format` before every commit.

### TypeScript / Angular Style

- **Naming:** `camelCase` for functions/vars, `PascalCase` for types/components
- **Standalone components:** No `NgModule` declarations; use `standalone: true`
- **Signals:** Use computed signals over imperative change detection
- **Observable:** For SSE and HTTP; subscribe only in templates with `async` pipe
- **Testing:** Component creation + template assertions; stub services with `vi.spyOn`

Run Prettier on every commit (or set editor to format on save).

### Review Expectations

- **Security:** No hardcoded secrets, API keys, or PII
- **Test coverage:** Unit tests for new logic; integration tests for API changes; E2E for user flows
- **Performance:** No N+1 queries; cache considerations documented
- **Backward compatibility:** API changes noted; migrations tested
- **Docs:** README/CLAUDE.md updated if behavior changes

---

## Pull Request Checklist

Before submitting a PR, verify:

- [ ] **Build passes:** `dotnet build` succeeds with 0 errors
- [ ] **Code formatted:** `dotnet format` applied (backend); Prettier run (frontend)
- [ ] **Architecture tests pass:** No layer violations or CQRS convention breaks
  ```bash
  dotnet test tests/Hookbin.ArchitectureTests/
  ```
- [ ] **Unit tests added/updated:** All new logic covered; existing tests still pass
  ```bash
  dotnet test tests/Hookbin.UnitTests/
  cd frontend/hookbin-spa && npm test
  ```
- [ ] **Integration tests run:** If touching API/database
  ```bash
  dotnet test tests/Hookbin.IntegrationTests/
  ```
- [ ] **E2E tests pass:** If touching user workflows
  ```bash
  pwsh scripts/rebuild-and-wait.ps1
  E2E_BASE_URL=http://localhost:8088 E2E_AUTH_PASSWORD=Admin123! dotnet test tests/Hookbin.E2ETests/
  ```
- [ ] **API contract updated:** If adding/changing endpoints
  - Update README.md §7.3 API Contract table
  - Add test case to integration/E2E suite
- [ ] **Environment variables documented:** If adding new config vars
  - Add row to README.md §10 Configuration Reference
  - Add to `.env.example` with description
- [ ] **SSE wire contract checked:** If modifying event names or `headers` type
  - Update CLAUDE.md DANGER ZONE section
  - Ensure frontend listener updated
- [ ] **No secrets in code:** Grep for `password=`, `token=`, `key=`, `secret=`
- [ ] **docker compose up -d works:** Full stack boots clean
  ```bash
  docker compose down -v  # wipe volumes first
  docker compose up -d
  ```
- [ ] **SEQ logs available:** Navigate to `http://localhost:5342` and confirm logs appear

---

## Common Development Tasks

### Adding a CQRS Command

See CLAUDE.md "Feature Recipe: Adding a CQRS Command" for the full pattern.

**Quick checklist:**
1. Create `src/Hookbin.Application/<Feature>/Commands/<Name>/`
2. Add `<Name>Command.cs` (`sealed record` implementing `IRequest<T>`)
3. Add `<Name>CommandHandler.cs` (`internal sealed class` implementing `IRequestHandler`)
4. Add `<Name>CommandValidator.cs` (`public sealed class` extending `AbstractValidator`)
5. Add controller action in appropriate controller
6. Add unit tests in `tests/Hookbin.UnitTests/Application/`
7. **Verify architecture tests pass:** `dotnet test tests/Hookbin.ArchitectureTests/`
   - Fails if command is not `sealed record`, handler is not `internal sealed`, validator doesn't extend `AbstractValidator`, or folder/namespace drifts

### Adding a Query Handler

Same pattern as commands:
1. Create `src/Hookbin.Application/<Feature>/Queries/<Name>/`
2. Add `<Name>Query.cs` (record: `IRequest<TResult>`)
3. Add `<Name>QueryHandler.cs` (implements `IRequestHandler<,TResult>`)
4. Validator is optional for read-only queries
5. Add tests

### Adding a Modal/Dialog (Angular)

Angular Material is removed — dialogs use the custom `ModalService` over `@angular/cdk` Overlay.

1. Create a standalone component implementing the dialog template (use `.dialog`, `.dialog-header`, `.dialog-body`, `.dialog-footer` CSS conventions; see `src/app/shared/confirm-dialog/`).
2. Inject `ModalRef` via `inject<ModalRef<TResult>>(MODAL_REF)` and call `this.ref.close(result)` on confirm/cancel.
3. For dialogs that need input data, also inject `MODAL_DATA`.
4. To open it from a component: `inject(ModalService).open(MyDialogComponent, { data: { ... } })`.
5. **Cancel must close with `null`, not empty string** — callers use `result == null` to detect cancellation. Use `[mat-dialog-close]="null"` on the Cancel button.
6. E2E selector for the dialog container: `.modal-panel` (NOT `mat-dialog-container`).

### Calling the API from non-Angular Clients (CSRF)

The API uses double-submit-cookie CSRF protection. **All state-changing requests** (`POST`/`PUT`/`PATCH`/`DELETE`) require the `X-XSRF-TOKEN` header to match the `XSRF-TOKEN` cookie. Angular's `HttpClient` handles this automatically.

For raw `fetch`, curl scripts, Postman, etc.:

```js
// 1. Make any authenticated GET request first — the response sets XSRF-TOKEN cookie
await fetch('/api/tokens', { credentials: 'include' });
// 2. Read the cookie
const xsrf = document.cookie.match(/XSRF-TOKEN=([^;]+)/)?.[1];
// 3. Include it as a header on writes
await fetch('/api/tokens', {
  method: 'POST',
  credentials: 'include',
  headers: { 'content-type': 'application/json', 'X-XSRF-TOKEN': decodeURIComponent(xsrf) },
  body: JSON.stringify({ name: 'test' })
});
```

The unauthenticated webhook receiver (`POST /webhook/{guid}`) does **not** require CSRF — it's `[AllowAnonymous]`.

### Modifying the Database Schema

1. Make your domain entity changes
2. Run:
   ```bash
   dotnet ef migrations add DescriptiveName \
     --project src/Hookbin.Infrastructure \
     --startup-project src/Hookbin.API
   ```
3. Review the generated migration in `src/Hookbin.Infrastructure/Persistence/Migrations/`
4. If destructive (drops/renames), add manual safety checks
5. Test locally:
   ```bash
   dotnet ef database update
   dotnet test tests/Hookbin.IntegrationTests/
   ```
6. Commit both the migration and your code changes together

### Updating Documentation

- **User guide / features:** Edit `README.md`
- **Agent guide / troubleshooting:** Edit `CLAUDE.md`
- **Codemaps:** Edit files in `docs/CODEMAPS/`
- **Development:** Edit this file (`docs/CONTRIBUTING.md`)
- **Deployment / operations:** Edit `docs/RUNBOOK.md`

Docs are the source of truth — keep them in sync with code changes.

---

## Getting Help

- **Architecture questions:** See CLAUDE.md or README.md §5–7
- **API contract:** See README.md §7.3
- **Design decisions:** See README.md §16
- **Troubleshooting:** See README.md §20
- **Testing guidance:** See README.md §13
- **Codemaps:** See `docs/CODEMAPS/INDEX.md`

---

## Questions?

Open an issue or ask in the PR. We're here to help.

Happy coding!
