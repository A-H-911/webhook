<!-- Generated: 2026-05-13 | Files scanned: 8 .csproj + 1 package.json | Token estimate: ~850 -->

# Dependencies

## Backend (.NET 10 / Hookbin.* projects)

### Domain (`src/Hookbin.Domain/`)
_No external packages — pure C#._

### Application (`src/Hookbin.Application/`)
| Package | Version | Purpose |
|---|---|---|
| `MediatR` | 14.1.0 | CQRS pipeline (`IRequestHandler`, behaviors) |
| `FluentValidation.AspNetCore` | 11.3.1 | Validators on commands/queries — pipeline behavior maps `ValidationException` → HTTP 422 |

### Infrastructure (`src/Hookbin.Infrastructure/`)
| Package | Version | Purpose |
|---|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.7 | EF Core SQL Server provider; `EnableRetryOnFailure(3,2s)` |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.7 | Migration tooling (also in API; **excluded** from worker `.csproj` — enforced by `OperationalSnapshotTests`) |
| `StackExchange.Redis` | 2.8.16 | Redis client — streams, pub/sub, cache, sets |
| `MediatR` | 14.1.0 | CQRS pipeline |
| `MaxMind.GeoIP2` | 5.4.1 | IP-to-country lookup (GeoIp service) |
| `Polly` | 8.6.6 | Retry policies (DB readiness) |
| `Microsoft.Extensions.Configuration.Binder` | 10.0.0 | Options binding |
| `Microsoft.Extensions.Hosting.Abstractions` | 10.0.0 | `IHostedService`, `BackgroundService` |

### API (`src/Hookbin.API/`)
| Package | Version | Purpose |
|---|---|---|
| `AspNetCore.HealthChecks.Redis` | 9.0.0 | `/health/ready` Redis probe |
| `AspNetCore.HealthChecks.SqlServer` | 9.0.0 | `/health/ready` SQL probe |
| `BCrypt.Net-Next` | 4.* | Password hash for `AUTH_PASSWORD_HASH` |
| `Microsoft.AspNetCore.OpenApi` | 10.0.5 | OpenAPI metadata |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.7 | `dotnet ef` migration tooling |
| `Polly` | 8.6.6 | Resilience (paired with Infrastructure usage) |
| `Serilog.AspNetCore` | 10.0.0 | Structured logging |
| `Serilog.Sinks.Seq` | 9.0.0 | SEQ sink |
| `Swashbuckle.AspNetCore` | 10.1.7 | Swagger UI generator |

### Workers (`Hookbin.StreamWorker` + `Hookbin.JobsWorker`)
Same Polly + Serilog + HealthChecks subset as API.
- `Microsoft.Extensions.Hosting.WindowsServices` 10.0.0 — Windows service hosting profile
- **Excludes** `Microsoft.EntityFrameworkCore.Design` (asserted by `OperationalSnapshotTests`)

### Test Projects (`tests/`)
| Project | Key packages |
|---|---|
| `Hookbin.UnitTests` | xUnit 2.9.3, NSubstitute, FluentAssertions 8.9.0 |
| `Hookbin.IntegrationTests` | xUnit, `Microsoft.AspNetCore.Mvc.Testing` 10.0.7, `Testcontainers.MsSql` 4.4.0, `Testcontainers.Redis` 4.4.0, `StackExchange.Redis` 2.8.16, `BCrypt.Net-Next`, FluentAssertions 8.9.0 |
| `Hookbin.ArchitectureTests` | xUnit 2.9.3, `TngTech.ArchUnitNET` 0.13.3 + `.xUnit` 0.13.3, `NetArchTest.eNhancedEdition` 1.4.5, FluentAssertions 8.9.0 |
| `Hookbin.E2ETests` | xUnit 2.9.3, `Microsoft.Playwright` 1.52.0, coverlet 6.0.4 |

`Microsoft.NET.Test.Sdk` 17.14.1 and `xunit.runner.visualstudio` 3.1.4 across all test projects.

**Mutation testing (dev tool, not in csproj):** `dotnet-stryker` 4.14.1 — Stryker.NET. Run via `dotnet stryker --project Hookbin.<X>.csproj`. Results: `StrykerOutput/<timestamp>/reports/mutation-report.html`.

## Frontend (Angular 21 — `frontend/hookbin-spa/`)

### Runtime
| Package | Version | Purpose |
|---|---|---|
| `@angular/core` (+ animations, common, compiler, forms, platform-browser, router) | ^21.2.0 | Angular framework |
| `@angular/cdk` | ^21.2.9 | CDK Overlay — backs custom `ModalService` (Angular Material removed) |
| `rxjs` | ~7.8.0 | Reactive streams (HttpClient, SSE wrapper) |
| `tslib` | ^2.3.0 | TypeScript runtime helpers |

### Dev
| Package | Version | Purpose |
|---|---|---|
| `@angular/build` + `@angular/cli` | ^21.2.9 | Angular build system |
| `@angular/compiler-cli` | ^21.2.0 | AOT compilation |
| `vitest` | ^4.0.8 | Test runner (Jasmine/Karma removed) |
| `@vitest/coverage-v8` | ^4.1.5 | V8 coverage (gates 80/75/76/80 line/branch/function/statement) |
| `jsdom` | ^28.0.0 | DOM environment for Vitest |
| `prettier` | ^3.8.1 | Code formatter |
| `typescript` | ~5.9.2 | TypeScript compiler |

**Removed since prior codemap:** `@angular/material`, `@angular/material-components-web`, `karma`, `jasmine-core`, `@types/jasmine`, `@angular-devkit/build-angular` (replaced by `@angular/build`).

## Docker Compose Services (`docker-compose.yml`)
| Service | Image / build | Notes |
|---|---|---|
| `api` | `./Dockerfile` (PROJECT_NAME=Hookbin.API) | runs migrations on startup |
| `stream-worker` | `./Dockerfile` (PROJECT_NAME=Hookbin.StreamWorker) | `HOOKBIN_WORKER_ID=stream-worker-1` |
| `jobs-worker` | `./Dockerfile` (PROJECT_NAME=Hookbin.JobsWorker) | `deploy: { replicas: 1 }` |
| `frontend` | `docker/frontend/Dockerfile` | nginx serving Angular bundle + reverse-proxy |
| `sqlserver` | `docker/sqlserver/Dockerfile` (mcr.microsoft.com/mssql/server:2022-latest base) | custom entrypoint runs `init.sql` |
| `redis` | `redis:7-alpine` | bind to 127.0.0.1 |
| `seq` | `datalust/seq:latest` | ingest 5341, UI 5342 (both localhost only) |
| `ngrok` | `ngrok/ngrok:latest` (override only) | dev-only tunnel |

## External Services
| Service | Use | Where |
|---|---|---|
| MaxMind GeoLite2 | IP-to-country in `IGeoIpService` | Optional — `MAXMIND_DB_PATH` env |
| ngrok | Public webhook URLs in dev | `docker-compose.ngrok.yml` override |
| Shields.io | README tech badges | `https://img.shields.io/badge/...` |

## Versions Snapshot (2026-05-13)
- .NET SDK preview: 10.0.300-preview.0.26177.108 (`NETSDK1057` warning expected)
- Node `packageManager`: npm@11.12.1
- All `Hookbin.*` projects target `net10.0`
