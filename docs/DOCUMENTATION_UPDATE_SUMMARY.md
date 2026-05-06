# Documentation Update Summary — 2026-05-06

All documentation has been synchronized with recent codebase changes.

## Files Updated

### 1. README.md (Single Source of Truth)
The comprehensive architecture and design guide for end users, operators, developers, and architects.

**Changes:**
- **Flow A (Webhook Receive)**: Updated to document:
  - `GetByTokenIncludingInactiveAsync` instead of filtered lookup
  - IOException wrapping with BadHttpRequestException
  - Always persist requests (audit trail for inactive)
  - 410 Gone response for inactive tokens with no SSE notify
  
- **API Contract (Webhook Receiver table)**: 
  - Added explicit documentation of 410 Gone response
  - Noted audit trail persistence for inactive tokens

- **WebhookRequest schema (ReceivedAt)**:
  - Updated to `DateTimeOffset(7)` with millisecond precision notation
  - Added migration reference: 20260506202000_PinReceivedAtPrecision

- **Token Cache Strategy (§7.7)**:
  - Clarified 5-minute sliding expiration vs previous 60-second reference
  - Noted both active and inactive tokens cached (receiver path requirement)
  - Added UpdateTokenCommand to cache invalidation list

- **Error Handling (new table after Validation Errors)**:
  - `OperationCanceledException` → silent (normal SSE disconnect)
  - `ValidationException` → 422 with field errors
  - `BadHttpRequestException` → 400 or 413 (logged)
  - Unhandled `Exception` → 500 (logged, not exposed)
  - Note about `context.Response.HasStarted` guard for SSE safety

- **Infrastructure file comments**:
  - Updated `WebhookTokenRepository` → includes `GetByTokenIncludingInactiveAsync`
  - Updated `WebhookRequestRepository` → includes `ThenByDescending(Id)` for determinism
  - Updated `GlobalExceptionMiddleware` → BadHttpRequestException handling
  - Updated `Program.cs` → retry policy note

### 2. CLAUDE.md (Agent-Facing Guide)
Quick reference for developers implementing features in the webhook service.

**Changes:**
- **Token cache fact**: 
  - Clarified `IMemoryCache` with 5-minute sliding expiration
  - Noted both active and inactive tokens cached (receiver lookup requirement)
  - Added UpdateTokenCommand to mutation list

- **New fact: Inactive token audit trail**:
  - Receiver uses `GetByTokenIncludingInactiveAsync`
  - Always persists requests (audit trail)
  - Returns 410 Gone but request is recorded

- **Updated Repository reads fact**:
  - Added note about `ThenByDescending(Id)` for deterministic pagination

- **New Bad request handling fact**:
  - `BadHttpRequestException` handling (400/413)
  - Logged with method/path context
  - IOException on body read converts to BadHttpRequestException

- **New invariants added**:
  - Always persist WebhookRequest from inactive tokens (audit trail)
  - Receiver must use `GetByTokenIncludingInactiveAsync` (not `GetByTokenAsync`)

### 3. Codemaps (docs/CODEMAPS/)
Architecture reference documents (updated in previous pass).

- `backend.md` — Exception handling, 410 Gone flow, repo methods
- `data.md` — Millisecond precision, deterministic ordering, inactive token behavior
- `frontend.md` — Timestamp display (HH:mm:ss.SSS)
- `architecture.md` — 410 Gone flow, resilience config
- `CHANGES_2026_05_06.md` — Change reference document (new)

## Cross-References

All documentation now cross-references correctly:
- README.md (§6 Flow A) → detailed webhook receive process
- README.md (§7.7 Token Cache) → cache strategy and lifecycle
- README.md (§7.3 Error Handling) → exception mapping
- README.md (§8 Solution Structure) → file locations and comments
- CLAUDE.md (Key facts) → non-obvious implementation details
- Codemaps — architectural overviews

## Key Facts Documented

### Inactive Token Handling (410 Gone)
- **Request**: Always persisted to database (audit trail)
- **Response**: 410 Gone (signals sender to stop retrying)
- **SSE**: No notification sent to subscribers
- **Cache**: Token cached for fast 410 response (prevents DB hit)

### Timestamp Precision
- **Database**: `DateTimeOffset(7)` — 7-digit fractional seconds (milliseconds)
- **Frontend**: All three surfaces show `HH:mm:ss.SSS`
- **Migration**: `20260506202000_PinReceivedAtPrecision` applied on startup

### Exception Handling
- **BadHttpRequestException**: 400 or 413 per status code, logged
- **ValidationException**: 422 Unprocessable Entity
- **OperationCanceledException**: Silent (normal SSE disconnect)
- **Unhandled**: 500 Internal Server Error (logged, not exposed)

### Deterministic Pagination
- **Order by**: `ReceivedAt DESC, THEN Id DESC`
- **Purpose**: Consistent results even with same-millisecond timestamps

### Database Resilience
- **Connection retry**: `EnableRetryOnFailure(3, 2s)` on transient failures
- **Automatic exponential backoff**: 1s → 2s between retries

## No Breaking Changes

- All endpoints remain at same paths
- All cache keys unchanged
- SSE wire protocol unchanged (still "event: request")
- Database migrations auto-applied on startup
- 410 Gone is new response code (callers should respect it)

## Verification Checklist

- [x] Codemaps updated with new behaviors
- [x] README sections 6 and 7 updated
- [x] CLAUDE.md facts updated
- [x] 410 Gone behavior documented
- [x] Inactive token audit trail documented
- [x] Millisecond precision documented
- [x] Exception handling documented
- [x] Deterministic pagination documented
- [x] No stale references remaining
- [x] All cross-references verified

## Generated

**Date**: 2026-05-06  
**Generator**: /everything-claude-code:update-docs  
**Status**: Complete
