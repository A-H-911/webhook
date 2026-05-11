<!-- Generated: 2026-05-11 | Verified: 118 tests (92.38% coverage) in 9 spec files; parsedQueryParams/parsedFormValues/threatLinks computed signals; note inline edit UX; processingTimeMs chip; Vitest migration; updateNote service method -->

# Frontend Architecture

## Stack
Angular 21 · Angular Material · standalone components · signals · cookie-based auth · SSE

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
    ├── query params kv-table (parsedQueryParams computed signal)
    ├── form values kv-table (parsedFormValues — form/urlencoded only)
    ├── threat intelligence links (threatLinks: Whois/Shodan/VirusTotal/Censys)
    ├── processing time chip (processingTimeMs — only when non-null)
    ├── note inline editor (noteEditing signal, MatSnackBar on error)
    ├── CustomResponseDialogComponent (mat-dialog)
    └── SseService.connect() → live request feed
```

## Services
```
AuthService     — login/logout/me, initialize() via APP_INITIALIZER, isLoggedIn signal
TokenService    — CRUD /api/tokens, custom-response PUT/DELETE
RequestService  — GET paginated, GET by id, export, clear, delete, PATCH note
SseService      — EventSource wrapper, withCredentials:true, exponential backoff
ThemeService    — light/dark toggle, persisted to localStorage
```

## Computed Signals (TokenDetailComponent)
```
parsedQueryParams()  → URLSearchParams.entries() from selectedRequest().queryString
                       Returns [] when queryString is null/empty

parsedFormValues()   → Parses body as URLSearchParams when ContentType includes
                       "application/x-www-form-urlencoded"; base64-decodes body first
                       when isBodyBase64===true; returns [] for non-form requests

threatLinks()        → { whois, shodan, virustotal, censys } URLs built from
                       selectedDetail().ipAddress; encodeURIComponent applied;
                       returns null hrefs when IP is null/"unknown"
```

## Note Inline Edit (TokenDetailComponent)
```
noteEditing: WritableSignal<boolean>   — true = textarea visible
noteValue:   WritableSignal<string>    — two-way bound to textarea

startNoteEdit()   → noteEditing.set(true), pre-fills noteValue from selectedDetail().note
cancelNoteEdit()  → noteEditing.set(false)
saveNote()        → RequestService.updateNote(tokenId, requestId, noteValue.trim() || null)
                    → PATCH /api/tokens/{tokenId}/requests/{id}/note
                    → refreshes selectedRequest signal on success
                    → MatSnackBar error on failure
```

## ProcessingTime Display
```
@if (selectedDetail().processingTimeMs !== null) {
  <span>{{ selectedDetail().processingTimeMs }} ms</span>
}
Set by StreamWorker after persist — null until stream-worker processes the entry.
```

## Auth Guard
```
authGuard → AuthService.isLoggedIn() → true: pass | false: navigate('/login')
APP_INITIALIZER → AuthService.initialize() → GET /api/auth/me (swallows errors)
```

## HTTP Interceptor & Logout
```
httpErrorInterceptor:
  401 response + path not /api/auth/ → AuthService.clearSession() + navigate('/login')

Logout button:
  Top toolbar (conditional on auth.isAuthenticated()) → logout() → router.navigate(['/login'])
  AuthService.logout() → POST /api/auth/logout + clearSession()
```

## Custom Response Dialog
```
Headers field: user enters raw JSON string "{\"X-Foo\":\"bar\"}"
→ Dialog validates via JSON.parse()
→ Sent as string (NOT object) to PUT /api/tokens/{id}/custom-response
→ Backend deserializes headers JSON to Dictionary<string,string>
```

## SSE Wire Protocol
```
EventSource /api/tokens/{id}/sse (withCredentials: true)
  onopen                         → emit { eventType: 'connected' }
  addEventListener('request')    → emit { eventType: 'new-request', data: RequestSummary }
  addEventListener('token-deleted') → emit { eventType: 'token-deleted' } + complete
  onerror                        → emit { eventType: 'disconnected' } + exponential reconnect (1s→30s)
```

## Timestamp Display
All three surfaces display millisecond precision (HH:mm:ss.SSS):
```
Dashboard token list:   {{ token.createdAt | date:'MMM d, y, HH:mm:ss.SSS' }}
Request list (compact): {{ req.receivedAt | date:'HH:mm:ss.SSS' }}
Request detail panel:   {{ selectedDetail().receivedAt | date:'MMM d, y, HH:mm:ss.SSS' }}
```

## Testing
```
Framework: Vitest ^4.0.8 via @angular/build:unit-test (NOT Karma/Jasmine)
Spec files (9): app, theme.service, auth.service, token.service, request.service,
                sse.service, confirm-dialog, custom-response-dialog, token-detail
Total tests: 118 (all green)
Coverage:    92% stmt / 84% branch / 90% fn / 93% line
Thresholds:  80/75/80/80 (stmt/branch/fn/line) — enforced in angular.json
Run:         cd frontend/hookbin-spa && npm test -- --watch=false --coverage
```

## Key Files
```
frontend/hookbin-spa/src/main.ts
frontend/hookbin-spa/src/app/app.config.ts                                  (providers, APP_INITIALIZER)
frontend/hookbin-spa/src/app/app.routes.ts                                  (route table)
frontend/hookbin-spa/src/app/core/services/auth.service.ts
frontend/hookbin-spa/src/app/core/services/sse.service.ts
frontend/hookbin-spa/src/app/core/services/token.service.ts
frontend/hookbin-spa/src/app/core/services/request.service.ts              (includes updateNote)
frontend/hookbin-spa/src/app/core/guards/auth.guard.ts
frontend/hookbin-spa/src/app/core/interceptors/http-error.interceptor.ts
frontend/hookbin-spa/src/app/core/models/request-detail.model.ts           (processingTimeMs, note fields)
frontend/hookbin-spa/src/app/features/dashboard/dashboard.component.ts
frontend/hookbin-spa/src/app/features/dashboard/create-token-dialog.component.ts
frontend/hookbin-spa/src/app/features/token-detail/token-detail.component.ts  (computed signals, note UX)
frontend/hookbin-spa/src/app/features/token-detail/token-detail.component.html (kv-tables, threat-links, note)
frontend/hookbin-spa/src/app/features/custom-response/custom-response-dialog.component.ts
frontend/hookbin-spa/src/app/services/theme.service.ts
frontend/hookbin-spa/angular.json                                           (coverageThresholds, coverageExclude)
```
