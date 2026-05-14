# Environment Variables Reference

<!-- AUTO-GENERATED:HEADER START -->
**Last Updated:** 2026-05-14 (re-verified against `.env.example` — no variable drift; 10/10 vars in sync; no new env vars introduced by hard-delete or StreamWorker FK fixes)
<!-- AUTO-GENERATED:HEADER END -->

This document describes all environment variables used by the Hookbin stack. Copy `.env.example` to `.env` and configure before deployment.

---

## Quick Reference

<!-- AUTO-GENERATED:ENV-TABLE START — source: .env.example -->

| Variable | Required | Default | Purpose |
|----------|:---:|---|---|
| `SA_PASSWORD` | ✅ | — | SQL Server `sa` account password (complexity required) |
| `HOOKBIN_BASE_URL` | ✅ | — | Public base URL for generated webhook URLs (no trailing slash) |
| `AUTH_USERNAME` | ➖ | `admin` | Single-admin login name |
| `AUTH_PASSWORD_HASH` | ✅ | — | **BCrypt hash** — see "BCrypt escaping" below |
| `AUTH_SESSION_HOURS` | ➖ | `8` | Cookie session duration in hours |
| `RETENTION_DAYS` | ➖ | `7` | Days before captured requests are deleted by JobsWorker |
| `MAX_REQUEST_SIZE_MB` | ➖ | `5` | Webhook receiver body size limit (returns 413 if exceeded) |
| `WEBHOOK__ReceiverRateLimitPerSecond` | ➖ | `250` | Token-bucket rate limit per webhook token (1–10000) |
| `HOOKBIN_WORKER_ID` | ➖ | `stream-worker-1` | Stable consumer name for Redis stream PEL recovery |
| `NGROK_AUTHTOKEN` | ➖ | — | Required only when using `docker-compose.ngrok.yml` |

<!-- AUTO-GENERATED:ENV-TABLE END -->

```bash
# Required (no defaults)
SA_PASSWORD=P@ssw0rd!SafeValue2024
HOOKBIN_BASE_URL=http://localhost:8088
AUTH_PASSWORD_HASH=<generated-by-tools/RotatePassword, then $-escaped — see below>

# Optional (defaults provided)
RETENTION_DAYS=7
MAX_REQUEST_SIZE_MB=5
WEBHOOK__ReceiverRateLimitPerSecond=250
AUTH_USERNAME=admin
AUTH_SESSION_HOURS=8
# HOOKBIN_WORKER_ID=stream-worker-1   (only set for multi-replica)
```

---

## SQL Server

### `SA_PASSWORD`

**Required:** Yes  
**Default:** None  
**Description:** SQL Server system administrator password for the `sa` account.

**Requirements:**
- Minimum 8 characters
- Must contain uppercase, lowercase, number, and special character
- Examples: `P@ssw0rd!SafeValue2024`, `MyS3cur3P@ss!`

**Notes:**
- Set once; changing it requires database recreation
- Store securely (use a secret manager in production)
- Never commit to source control

---

## Webhook Configuration

### `HOOKBIN_BASE_URL`

**Required:** Yes  
**Default:** None (app refuses to start without it)  
**Description:** The public-facing base URL used to construct webhook URLs shown in the UI.

**Examples:**
- Local Docker: `http://localhost:8088`
- Production: `https://webhook.example.com`
- ngrok tunnel: `https://abc123.ngrok.app`

**Notes:**
- Do NOT include trailing slash
- The full webhook URL is `{HOOKBIN_BASE_URL}/webhook/{guid}`
- Changing this immediately affects URLs shown in the UI (recomputed at read time, not stored per-token)
- Used by receivers to POST requests back; must be reachable from external callers

### `WEBHOOK__ReceiverRateLimitPerSecond`

**Required:** No  
**Default:** `250`  
**Description:** Rate limit (requests per second) applied to the webhook receiver endpoint per token.

**Range:** 1–10,000  
**Notes:**
- Prevents token from being flooded with requests
- Applied per-token (not global)
- Requests exceeding the limit receive HTTP 429 Too Many Requests

---

## Authentication

### `AUTH_USERNAME`

**Required:** No  
**Default:** `admin`  
**Description:** Login username for the single admin account.

**Notes:**
- Single account only (no multi-user support)
- Typically `admin` or environment-specific name

### `AUTH_PASSWORD_HASH`

**Required:** Yes  
**Default:** None  
**Description:** BCrypt-hashed password for the admin account.

**Format:** Must be a valid BCrypt hash starting with `$2` (e.g., `$2a$12$...`)

**How to Generate:**

```bash
# Option 1: Using the RotatePassword tool (requires .NET 10)
dotnet run --project tools/RotatePassword -- --password "YourSecurePassword123!"

# Option 2: Using Python
python3 -c "import bcrypt; print(bcrypt.hashpw(b'YourSecurePassword123!', bcrypt.gensalt(12)).decode())"

# Option 3: Using Node.js + bcryptjs
node -e "const bcrypt = require('bcryptjs'); bcrypt.hash('YourSecurePassword123!', 12, (err, hash) => console.log(hash));"
```

**Notes:**
- Never store plaintext passwords
- Always use BCrypt (12+ rounds)
- The app validates the `$2` prefix at startup; plaintext hashes cause immediate failure
- To rotate: generate new hash and update this variable + restart API

### `AUTH_SESSION_HOURS`

**Required:** No  
**Default:** `8`  
**Description:** Session timeout in hours. Sessions expire after this duration of inactivity.

**Notes:**
- Minimum recommended: `1` (1 hour)
- Maximum recommended: `24` (1 day)
- User must re-login after timeout

---

## Data Retention

### `RETENTION_DAYS`

**Required:** No  
**Default:** `7`  
**Description:** Number of days to retain captured webhook requests before automatic cleanup.

**Notes:**
- Cleanup runs every 24 hours (in `jobs-worker`)
- Requests older than this value are permanently deleted
- Change takes effect on next cleanup cycle
- Recommended range: 1–90 days

---

## Request Limits

### `MAX_REQUEST_SIZE_MB`

**Required:** No  
**Default:** `5`  
**Description:** Maximum size of a webhook request body in megabytes.

**Notes:**
- Enforced by Kestrel (ASP.NET Core HTTP server)
- Requests exceeding this limit receive HTTP 413 Payload Too Large
- Includes headers and query string
- Recommended range: 1–100 MB

---

## Redis (Stream Worker)

### `HOOKBIN_WORKER_ID`

**Required:** No  
**Default:** `stream-worker-1` (in docker-compose.yml)  
**Description:** Stable consumer identity for the Redis stream consumer group.

**Notes:**
- Used by `StreamWorker` to recover pending messages (PEL) on restart
- Should be static across container restarts to maintain PEL consistency
- If this changes, old pending messages are orphaned permanently
- Only override in multi-replica scenarios; single-instance default is fine

### `ConnectionStrings__Redis`

**Required:** Yes (for `stream-worker`)  
**Default:** `redis:6379` (in docker-compose.yml)  
**Description:** Redis connection string for stream consumer and token cache.

**Format:** `hostname:port` or `hostname:port,hostname:port` (for sentinel/cluster)  
**Examples:**
- Docker Compose: `redis:6379`
- Local: `localhost:6379`
- Production: `redis-primary.internal:6379`

**Notes:**
- Only required if running `StreamWorker`
- API and JobsWorker use `IMemoryCache` (in-process)

---

## Database Connection

### `ConnectionStrings__DefaultConnection`

**Required:** No (auto-configured in Docker)  
**Default:** Computed from `SA_PASSWORD` in docker-compose.yml  
**Description:** SQL Server connection string for the application.

**Format:** `Server=<host>;Initial Catalog=WebhookDb;User ID=sa;Password=<password>;Trust Server Certificate=True;Encrypt=False`

**Notes:**
- In Docker Compose, this is auto-built from env vars
- For manual setup, follow the format above
- `Trust Server Certificate=True` is required for self-signed dev/test certs
- `Encrypt=False` is acceptable for internal Docker networks

---

## Logging

### `SERILOG__USING:*` / `SERILOG__* environment variables

The application uses Serilog for structured logging. All events are streamed to SEQ.

**Auto-configured in docker-compose.yml:**
```env
SERILOG__WriteTo:0__Name=Seq
SERILOG__WriteTo:0__Args__serverUrl=http://seq:5341
```

**Notes:**
- SEQ UI accessible at `http://localhost:5342` (localhost-only)
- Change `serverUrl` for remote SEQ instances
- Log level can be adjusted via `SERILOG__MinimumLevel`

---

## CORS & Security

### `CORS__AllowedOrigins`

**Required:** No  
**Default:** Empty (no CORS headers sent)  
**Description:** Comma-separated list of origins allowed to make cross-origin requests.

**Examples:**
- `http://localhost:4200` (Angular dev server)
- `http://localhost:4200,https://app.example.com`

**Notes:**
- Only applies to API routes (`/api/**`)
- Webhook receiver (`/webhook/**`) is always anonymous
- For development, add `http://localhost:4200` to enable Angular dev server

---

## Configuration Loading Order

The application loads environment variables in this order (later overrides earlier):

1. **appsettings.json** — Default values
2. **appsettings.{ASPNETCORE_ENVIRONMENT}.json** — Environment-specific defaults (e.g., `appsettings.Development.json`)
3. **.env file** — Environment variables from compose or manual source
4. **Environment variables** — Shell/system environment (highest priority)

**Example:** If `appsettings.json` sets `HOOKBIN_BASE_URL=http://localhost:5000` but `.env` sets `HOOKBIN_BASE_URL=http://localhost:8088`, the `.env` value wins.

---

## BCrypt Hash Escaping (`AUTH_PASSWORD_HASH`)

**Docker Compose v2 interprets `$letter` in `.env` files as a variable reference** and substitutes (or strips) it before the value reaches the container. BCrypt hashes contain literal `$` characters (`$2b$12$...`) — so they MUST be escaped or the API will boot with a corrupted hash and reject every login.

The project convention (encoded in `.env.example`) is to **double every `$`** in `.env`:

```env
# Generated by tools/RotatePassword:
#   $2b$12$AbCdEfGhIjKlMnOpQrStUv...
# After $$-escape (use this in .env):
AUTH_PASSWORD_HASH=$$2b$$12$$AbCdEfGhIjKlMnOpQrStUv...
```

The `$$` is read by Compose as a literal `$`. Inside the container, `AUTH_PASSWORD_HASH` is a valid BCrypt hash again.

**Generate + escape in one step:**

```bash
dotnet run --project tools/RotatePassword -- --password 'YourSecurePassword123!' \
  | sed 's/\$/$$/g'
```

Single-quoting (`AUTH_PASSWORD_HASH='$2b$...'`) also works on **bare** dotnet runs (no Compose), but Compose v2 still interpolates inside single quotes. **Use `$$`-escape** when going through `docker compose`.

## .env File General Rules

```env
# OK: $$-escape — Compose treats $$ as literal $
AUTH_PASSWORD_HASH=$$2a$$12$$xyz...

# PROBLEM: No quotes, no escape — $2 is interpreted as a variable name
AUTH_PASSWORD_HASH=$2a$12$xyz...  # → becomes a$12$xyz... (broken)

# OK: Quoted values containing spaces
HOOKBIN_BASE_URL="http://localhost/path with spaces"

# PROBLEM: Unquoted spaces
HOOKBIN_BASE_URL=http://localhost with spaces  # → broken
```

**Rule of thumb:** Double every `$` in hashes; quote any value with spaces or special characters.

---

## Example .env Files

### Development (Local Docker)

```env
SA_PASSWORD=Dev@12345!
HOOKBIN_BASE_URL=http://localhost:8088
RETENTION_DAYS=7
MAX_REQUEST_SIZE_MB=5
WEBHOOK__ReceiverRateLimitPerSecond=250
AUTH_USERNAME=admin
AUTH_PASSWORD_HASH=$$2b$$12$$...generated-then-escaped...
AUTH_SESSION_HOURS=8
CORS__AllowedOrigins=http://localhost:4200
```

### Production (Internet-facing)

```env
SA_PASSWORD=P@ssw0rd!SecureValue2024WithSymbols
HOOKBIN_BASE_URL=https://webhook.example.com
RETENTION_DAYS=30
MAX_REQUEST_SIZE_MB=10
WEBHOOK__ReceiverRateLimitPerSecond=500
AUTH_USERNAME=webhook-admin
AUTH_PASSWORD_HASH=$$2b$$12$$...generated-then-escaped...
AUTH_SESSION_HOURS=8
```

### Staging (ngrok tunnel)

```env
SA_PASSWORD=Staging!P@ss2024
HOOKBIN_BASE_URL=https://abc123def456.ngrok.app
RETENTION_DAYS=7
MAX_REQUEST_SIZE_MB=5
AUTH_USERNAME=staging-admin
AUTH_PASSWORD_HASH=$$2b$$12$$...generated-then-escaped...
AUTH_SESSION_HOURS=8
NGROK_AUTHTOKEN=ngrok_auth_token_here
```

---

## Validation & Startup Checks

The application validates the following at startup:

| Variable | Check | Failure Behavior |
|----------|-------|------------------|
| `HOOKBIN_BASE_URL` | Not empty | App refuses to start; logs `ValidateOptionsResult.Fail` |
| `AUTH_PASSWORD_HASH` | Starts with `$2` | App refuses to start; logs `The configured password does not match...` |
| `SA_PASSWORD` | Meets complexity (8+ chars, upper/lower/number/symbol) | SQL Server container exits; logs error |
| `RETENTION_DAYS` | Integer > 0 | App refuses to start |
| `MAX_REQUEST_SIZE_MB` | Integer > 0 | App refuses to start |

If any validation fails, the application will not start. Check logs and fix the env var before restarting.

---

## Common Mistakes

| Mistake | Impact | Fix |
|---------|--------|-----|
| `AUTH_PASSWORD_HASH` is plaintext | App won't start; "does not match expected format" | Generate BCrypt hash with `tools/RotatePassword` |
| `HOOKBIN_BASE_URL=http://localhost:8088/` (trailing slash) | URLs generated as `/webhook/{guid}/` | Remove the trailing slash |
| `SA_PASSWORD=weak` (no symbols) | SQL Server container exits | Use 8+ chars with upper/lower/number/symbol |
| `HOOKBIN_BASE_URL` not set | App won't start | Set to public-facing URL (even for dev, use `http://localhost:8088`) |
| `AUTH_PASSWORD_HASH=$2a$12$...` (unquoted in .env) | Hash corrupted ($ interpolated) | Quote: `AUTH_PASSWORD_HASH='$2a$12$...'` |
| Different `AUTH_PASSWORD_HASH` on frontend vs backend | Login always fails | Ensure both use same hash from `tools/RotatePassword` |

---

## Environment-Specific Notes

### Docker Compose

Environment variables can be set in:
1. `.env` file (highest priority — recommended)
2. `docker-compose.yml` `environment:` section
3. `docker-compose.override.yml` (dev overrides)
4. Shell environment

**To apply:** `docker compose up -d` (re-reads `.env` on each invocation)

### Direct .NET Execution (Development)

Environment variables loaded from:
1. `appsettings.json` and `appsettings.Development.json`
2. Shell environment (e.g., `export WEBHOOK__BaseUrl=http://localhost:8080`)

```bash
# Set env vars before running
export WEBHOOK__BaseUrl=http://localhost:8080
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/Hookbin.API
```

### Kubernetes Deployment

In a Kubernetes manifest, use:
```yaml
env:
  - name: HOOKBIN_BASE_URL
    value: "https://webhook.example.com"
  - name: AUTH_PASSWORD_HASH
    valueFrom:
      secretKeyRef:
        name: webhook-secrets
        key: auth-password-hash
```

---

## Security Best Practices

1. **Never commit `.env` to version control** — add `.env` to `.gitignore`
2. **Use a secret manager** — in production, source `AUTH_PASSWORD_HASH` and `SA_PASSWORD` from Vault, AWS Secrets Manager, Kubernetes Secrets, etc.
3. **Rotate credentials regularly** — `AUTH_PASSWORD_HASH` every 90 days minimum
4. **Restrict file permissions** — `.env` should be readable only by the owner (`chmod 600 .env`)
5. **Audit access** — log all login attempts via SEQ; review regularly

---

## Troubleshooting

### "The configured password does not match the expected format"

The `AUTH_PASSWORD_HASH` is not a valid BCrypt hash.

```bash
# Verify it starts with $2
grep AUTH_PASSWORD_HASH .env

# If plaintext or wrong format, regenerate
dotnet run --project tools/RotatePassword -- --password "YourPassword123!"
```

### "Webhook:BaseUrl must not be empty"

`HOOKBIN_BASE_URL` is not set.

```bash
# Add to .env
HOOKBIN_BASE_URL=http://localhost:8088

# Restart
docker compose up -d
```

### SQL Server "password does not meet the password requirements"

`SA_PASSWORD` does not meet complexity rules.

```bash
# Must be 8+ chars with upper, lower, number, symbol
SA_PASSWORD=P@ssw0rd!SafeValue2024
```

---

## References

- **Full configuration guide:** README.md §10 Configuration Reference
- **Docker Compose setup:** README.md §4 Quick Start
- **Deployment:** docs/RUNBOOK.md
- **Development:** docs/CONTRIBUTING.md
