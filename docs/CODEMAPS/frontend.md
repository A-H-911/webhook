<!-- Generated: 2026-05-13 | Files scanned: 61 frontend (TS/HTML/SCSS); 30 spec files | Token estimate: ~900 -->

# Frontend (Angular 21)

## Stack
- **Angular 21** — standalone components, signals, `@if`/`@for`/`@switch`/`@let` template syntax
- **@angular/cdk 21** — overlay used by custom `ModalService` (Material removed; only CDK remains)
- **Vitest 4** test runner (Jasmine/Karma removed)
- **EventSource API** for SSE (`withCredentials: true` for cross-origin auth in dev)
- **HttpClient** with `withInterceptors([httpErrorInterceptor])`, auto-attaches `X-XSRF-TOKEN` from `XSRF-TOKEN` cookie

## Routes (`src/app/app.routes.ts`)
```
''                  → redirect → 'dashboard'
'/login'            → LoginComponent
'/dashboard'        → DashboardComponent       [canActivate: authGuard]
'/tokens/:id'       → TokenDetailComponent     [canActivate: authGuard]
'**'                → redirect → 'dashboard'
```

## Folder Layout (`src/app/`)
```
core/
  guards/        authGuard (Router.navigate(['/login']))
  interceptors/  http-error.interceptor.ts     (401→/login, EXCLUDES /api/auth/* — DANGER ZONE)
  models/        token.model.ts, request-summary.model.ts, request-detail.model.ts,
                 dashboard-metrics.model.ts, page.model.ts
  services/      auth.service.ts (APP_INITIALIZER checkSession swallow),
                 token.service.ts, request.service.ts, dashboard.service.ts,
                 sse.service.ts ({ withCredentials: true }, addEventListener('request')),
                 breadcrumb.service.ts, version.service.ts
features/
  login/         LoginComponent
  dashboard/     DashboardComponent + create-token-dialog.component.ts
  token-detail/  TokenDetailComponent (signals: token, selectedDetail, bodyViewMode, noteValue)
  custom-response/ CustomResponseDialogComponent
services/        theme.service.ts (dark/light/system)
shared/
  modal/         ModalService (CDK Overlay), ModalRef, MODAL_REF, MODAL_DATA   ← replaces MatDialog
  toast/         ToastService (DOM-injected .toast)                            ← replaces MatSnackBar
  confirm-dialog/  ConfirmDialogComponent                                      ← replaces mat-dialog confirm
  json-tree/     JsonTreeComponent (tree view of parsed JSON body)
  json-highlight/  jsonHighlightPipe (syntax-highlight raw body)
  sparkline/     SparklineComponent (24h activity sparkline on dashboard card)
```

## Key Components
| Component | Highlights |
|---|---|
| `DashboardComponent` | Infinite-scroll `IntersectionObserver`, page size 50, `(click)=open(token)` navigates to `/tokens/{id}`; `openCreate()` → modal → on close navigates to detail page; `delete(token, $event)` confirm-modal pattern |
| `TokenDetailComponent` | Two-panel layout (list + detail); `.request-row` selection signal; body view modes (tree / pretty / raw); custom-response button → `CustomResponseDialogComponent`; note save on blur (`saveNote()` skips if unchanged) |
| `CreateTokenDialogComponent` | Single name field (`maxlength=80`) + description; `[mat-dialog-close]="null"` on Cancel — DANGER ZONE invariant (`null`, not `""`, so dashboard guard `result == null` rejects) |
| `CustomResponseDialogComponent` | Native `<select>` for status, raw JSON for headers; `headersControl` validator rejects non-object JSON; Save emits `{ action: 'save', dto }`, Remove emits `{ action: 'reset' }` |
| `ConfirmDialogComponent` | Generic OK/Cancel modal via `MODAL_DATA = { title, message, confirmLabel }` |
| `JsonTreeComponent` | Recursive tree of parsed body with `filter` (search) + `autoExpandTo` paths |

## State Management
- **Signals everywhere** — no NgRx / Akita. Components use `signal()`, `computed()`, `effect()`.
- **No global store**. Each feature service owns its slice via signals.
- **SSE → list update**: `TokenDetailComponent` subscribes to `SseService.connect(tokenId)`; on `eventType === 'new-request'` prepends to `requests()` signal.

## SSE Wire Contract
- Server emits `event: request\n` + `data: <summary-json>\n\n`
- Browser: `es.addEventListener('request', ev => ...)` — wire name `request`, mapped to `eventType: 'new-request'` for app code
- `onopen` emits synthetic `{ eventType: 'connected' }` so the green-dot indicator flips immediately
- Reconnect: on `onerror`, schedule new `EventSource` after 1s backoff (cancelled on `unsubscribe`)

## DANGER ZONE Invariants (pinned in tests)
| Invariant | Test |
|---|---|
| `SseService` constructs `EventSource` with `{ withCredentials: true }` | `sse.service.spec.ts` |
| Wire event name = `'request'` (server) ↔ `'new-request'` (app) | `sse.service.spec.ts` |
| `httpErrorInterceptor` EXCLUDES `/api/auth/*` from 401→/login redirect | `http-error.interceptor.spec.ts` |
| `APP_INITIALIZER checkSession()` swallows errors (no boot block) | `auth.service.spec.ts` |
| `[mat-dialog-close]="null"` on Cancel in `CreateTokenDialog` | `create-token-dialog` UI |

## Testing (Vitest)
| Spec area | Files |
|---|---|
| Services | `auth`, `dashboard`, `request`, `sse`, `token`, `breadcrumb`, `version`, `theme` |
| Interceptors | `http-error.interceptor.spec.ts` (DANGER ZONE pin) |
| Dialogs | `create-token-dialog.component.spec.ts`, `custom-response-dialog.component.spec.ts`, `confirm-dialog.component.spec.ts` |
| Modal infra | `modal.service.spec.ts`, `toast.service.spec.ts` |
| Pipes / shared | `json-highlight`, `json-tree`, `sparkline` |
| Features | `dashboard.component.spec.ts`, `token-detail.component.spec.ts`, `login.component.spec.ts` |
| **Total** | **209 tests** |

## Build / Dev
```
npm start              dev server :4200 (proxies /api, /webhook, /health → :8080)
npm run build          production bundle
npm test               vitest --watch=false
npm test -- --coverage Vitest coverage (gates: 80/75/76/80)
```

## Notable Files
| Path | Why it matters |
|---|---|
| `src/app/app.html` | Top-level app shell with brand mark SVG (matches `docs/assets/logo.svg`) |
| `src/app/core/interceptors/http-error.interceptor.ts` | CSRF + 401 handling — auth-path exclusion is a documented invariant |
| `src/app/core/services/sse.service.ts` | EventSource wrapper, reconnect logic, withCredentials |
| `src/app/shared/modal/modal.service.ts` | CDK Overlay-based modal with backdrop click + Escape close |
| `frontend/hookbin-spa/public/favicon.svg` | Brand mark (32x32); README uses scaled-up version at `docs/assets/logo.svg` |
