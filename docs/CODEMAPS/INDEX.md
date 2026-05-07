# Webhook Service — Codemaps Index

**Last Updated:** 2026-05-07 (all codemaps refreshed with rate limiting, antiforgery, session revocation, per-token SSE cap, custom response headers deserialization, HashGen tool rename, batched retention cleanup, search constraints)

---

## Overview

Webhook Inspector is a self-hosted webhook debugging platform built with:
- **.NET 10** backend (Clean Architecture: Domain → Application → Infrastructure → API)
- **Angular 21** frontend SPA (standalone components, Angular Material)
- **SQL Server 2022** with Testcontainers for integration testing
- **SSE** for real-time request notifications

---

## Codemap Files

| File | Purpose | Audience |
|------|---------|----------|
| **architecture.md** | System layers, dependency graph, deployment units, key decisions | Architects, reviewers |
| **backend.md** | .NET project structure, controllers, CQRS map, key classes | Backend developers |
| **frontend.md** | Angular structure, services, components, routing | Frontend developers |
| **data.md** | Database schema, entity relationships, data flows (receive, SSE, custom response) | Full-stack developers |
| **dependencies.md** | NuGet/npm packages, versions, purpose, why used | Reviewers, upgraders |

---

## Quick Navigation

### For Backend Developers
1. Read **architecture.md** for the big picture
2. Read **backend.md** for module layout and CQRS handlers
3. Cross-reference **data.md** for entity relationships
4. See **dependencies.md** for .NET packages

### For Frontend Developers
1. Read **architecture.md** for the big picture
2. Read **frontend.md** for component structure and routing
3. Read **data.md** for API contracts and SSE behavior
4. See **dependencies.md** for npm packages

### For Full-Stack / DevOps
1. Read **architecture.md** for deployment units
2. Read **data.md** for critical data flows
3. See **dependencies.md** for all external services

---

## Recent Changes (as of 2026-05-07)

### Backend Updates
- **WebhookController.cs** — custom response headers now deserialized and applied to responses
- **Program.cs** — rate limiting (`webhook-receiver` policy), antiforgery (`X-XSRF-TOKEN`), session revocation store, ForwardedHeaders
- **SseNotifier.cs** — per-token subscriber cap (max 10, returns 429 on 11th)
- **WebhookRequestRepository.cs** — batched retention cleanup (5k rows/loop), search constrained to Method/Path/IpAddress/StatusCode

### Frontend Updates
- **app.ts** + **app.html** — logout button wired to `AuthService`; conditional on `auth.isAuthenticated()`
- **token.service.ts** — `setCustomResponse` return type corrected to `Observable<void>` (backend returns 204)
- **token-detail.component.ts** — custom-response save rebuilds signal from DTO instead of calling `token.set(null)`
- **token-detail.component.html** — search empty state: "No requests yet" vs "No matching requests found"

### Infrastructure Updates
- **nginx.conf** — CSP updated to allow `fonts.gstatic.com` and `fonts.googleapis.com`
- **HashGen/** (renamed from `RotatePassword`) — BCrypt hash generator CLI
- **New tests** — `SessionRevocationStoreTests.cs`, `SseNotifierTests.cs`, `NoOpAntiforgery.cs`

---

## Key Entry Points

| Layer | File | Purpose |
|-------|------|---------|
| **API** | `src/WebhookService.API/Program.cs` | DI, middleware, endpoint mapping |
| **Controllers** | `src/WebhookService.API/Controllers/WebhookController.cs` | `POST /webhook/{token:guid}` receiver |
| **Middleware** | `src/WebhookService.API/Middleware/GlobalExceptionMiddleware.cs` | Exception → HTTP status, SSE guards |
| **Domain** | `src/WebhookService.Domain/Entities/WebhookToken.cs` | Token aggregate root |
| **Application** | `src/WebhookService.Application/Tokens/Commands/` | CQRS command handlers |
| **Infrastructure** | `src/WebhookService.Infrastructure/Sse/SseNotifier.cs` | Channel-based SSE broadcast |
| **Frontend** | `frontend/webhook-spa/src/app/app.ts` | Root Angular component, auth, logout |
| **Services** | `frontend/webhook-spa/src/app/core/services/sse.service.ts` | EventSource SSE client |

---

## References

- **Full README:** `README.md` (single source of truth for design, security, troubleshooting)
- **Agent guide:** `CLAUDE.md` (commands, key non-obvious facts)
- **Architecture:** README.md §5 HLD, §7 LLD
- **Data flows:** README.md §6
- **CQRS map:** README.md §7.5
- **Security model:** README.md §11
- **Testing guide:** README.md §13
- **Troubleshooting:** README.md §20
