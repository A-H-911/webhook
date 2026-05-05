<!-- Generated: 2026-05-05 | Files scanned: 21 | Token estimate: ~450 -->

# Frontend Architecture

## Stack
Angular 21 · Angular Material · standalone components · cookie-based auth · SSE

## Page Tree
```
/                → redirect → /dashboard
/login           → LoginComponent          (lazy)  [AllowAnonymous]
/dashboard       → DashboardComponent      (lazy)  [authGuard]
/tokens/:id      → TokenDetailComponent    (lazy)  [authGuard]
**               → redirect → /dashboard
```

## Component Hierarchy
```
AppComponent
├── LoginComponent
│   └── mat-card login form → AuthService.login()
├── DashboardComponent
│   ├── token list (mat-list)
│   ├── CreateTokenDialogComponent (mat-dialog)  ← [mat-dialog-close]="null" on Cancel
│   └── theme toggle → ThemeService
└── TokenDetailComponent
    ├── request list (paginated, searchable)
    ├── CustomResponseDialogComponent (mat-dialog)
    └── SseService.connect() → live request feed
```

## Services
```
AuthService     — login/logout/me, initialize() via APP_INITIALIZER, isLoggedIn signal
TokenService    — CRUD /api/tokens, custom-response PUT/DELETE
RequestService  — GET paginated, GET by id, export, clear, delete
SseService      — EventSource wrapper, withCredentials:true, exponential backoff
ThemeService    — light/dark toggle, persisted to localStorage
```

## Auth Guard
```
authGuard → AuthService.isLoggedIn() → true: pass | false: navigate('/login')
APP_INITIALIZER → AuthService.initialize() → GET /api/auth/me (swallows errors)
```

## HTTP Interceptor
```
httpErrorInterceptor:
  401 response + path not /api/auth/ → AuthService.clearSession() + navigate('/login')
```

## SSE Wire Protocol
```
EventSource /api/tokens/{id}/sse (withCredentials: true)
  onopen                         → emit { eventType: 'connected' }
  addEventListener('request')    → emit { eventType: 'new-request', data: RequestSummary }
  addEventListener('token-deleted') → emit { eventType: 'token-deleted' } + complete
  onerror                        → emit { eventType: 'disconnected' } + exponential reconnect (1s→30s)
```

## Key Files
```
frontend/webhook-spa/src/main.ts
frontend/webhook-spa/src/app/app.config.ts                                  (providers, APP_INITIALIZER)
frontend/webhook-spa/src/app/app.routes.ts                                  (route table)
frontend/webhook-spa/src/app/core/services/auth.service.ts
frontend/webhook-spa/src/app/core/services/sse.service.ts
frontend/webhook-spa/src/app/core/services/token.service.ts
frontend/webhook-spa/src/app/core/services/request.service.ts
frontend/webhook-spa/src/app/core/guards/auth.guard.ts
frontend/webhook-spa/src/app/core/interceptors/http-error.interceptor.ts
frontend/webhook-spa/src/app/features/dashboard/dashboard.component.ts
frontend/webhook-spa/src/app/features/dashboard/create-token-dialog.component.ts
frontend/webhook-spa/src/app/features/token-detail/token-detail.component.ts
frontend/webhook-spa/src/app/features/custom-response/custom-response-dialog.component.ts
frontend/webhook-spa/src/app/services/theme.service.ts
```
