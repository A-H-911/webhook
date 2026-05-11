# Operations Runbook

**Last Updated:** 2026-05-11

This guide covers deployment, health monitoring, troubleshooting, and operational procedures for the Webhook Service.

---

## Table of Contents

1. [Deployment](#deployment)
2. [Health Checks](#health-checks)
3. [Monitoring & Logs](#monitoring--logs)
4. [Common Issues & Solutions](#common-issues--solutions)
5. [Rollback Procedures](#rollback-procedures)
6. [Maintenance Tasks](#maintenance-tasks)
7. [Performance Tuning](#performance-tuning)

---

## Deployment

### Prerequisites

- Docker Engine 24+ with Compose v2
- 3 GB RAM available (SQL Server 2022 requires 2 GB alone)
- 5 GB disk free
- `.env` file configured with secrets

### Initial Deployment (Fresh Install)

```bash
# 1. Clone repository
git clone <repo-url>
cd webhook

# 2. Copy and configure environment variables
cp .env.example .env

# Edit .env with production values:
# - SA_PASSWORD: Strong SQL Server password (e.g., P@ssw0rd!SafeValue2024)
# - WEBHOOK_BASE_URL: Public-facing URL (e.g., https://webhook.example.com)
# - AUTH_PASSWORD_HASH: BCrypt hash (generate below)
# - RETENTION_DAYS: How long to keep request data (default: 7)
# - MAX_REQUEST_SIZE_MB: Maximum request size (default: 5)

# 3. Generate AUTH_PASSWORD_HASH (run locally or in container)
# Option A: Local (requires .NET 10)
dotnet run --project tools/RotatePassword -- --password "YourAdminPassword123!"

# Option B: Extract hash from temporary container
docker run --rm -v $(pwd):/app -w /app mcr.microsoft.com/dotnet/sdk:10 \
  dotnet run --project tools/RotatePassword -- --password "YourAdminPassword123!"

# Copy the generated hash into AUTH_PASSWORD_HASH in .env

# 4. Start the stack
docker compose up -d

# 5. Wait for SQL Server initialization (up to 45 seconds)
docker compose logs sqlserver | grep "SQL Server is now ready"

# 6. Verify all services are healthy
docker compose ps  # All should show "healthy" or "running"

# 7. Check API is responsive
curl http://localhost:8080/health/ready

# 8. Open the UI
# Navigate to http://localhost:8088 and log in with:
# - Username: admin
# - Password: <your configured password>
```

### Upgrade Existing Deployment

```bash
# 1. Back up data (if using named volumes)
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SA_PASSWORD" \
  -Q "BACKUP DATABASE [WebhookDb] TO DISK = '/var/opt/mssql/backup/WebhookDb_before_upgrade.bak'"

# 2. Pull latest code
git pull origin main

# 3. Rebuild and restart
docker compose build
docker compose up -d

# 4. Verify migrations ran
docker compose logs api | grep -i migration

# 5. Smoke test the API
curl http://localhost:8080/health/ready
```

### Zero-Downtime Update (Advanced)

For deployments where you cannot afford downtime:

```bash
# 1. Run migrations against a pre-warmed DB before deploying new code
docker compose exec api dotnet ef database update \
  --project src/WebhookService.Infrastructure \
  --startup-project src/WebhookService.API

# 2. Build new images
docker compose build

# 3. Scale up a second API instance (if multi-replica capable)
docker compose up -d api --scale api=2

# 4. Verify both instances healthy
docker compose ps | grep api

# 5. Drain traffic from old instance
docker compose stop api_1

# 6. Remove old instance
docker compose rm -f api_1
```

---

## Health Checks

### Endpoints

| Endpoint | Purpose | Expected Response |
|----------|---------|-------------------|
| `GET /health/live` | Liveness — is the process alive? | 200 OK (always, even if DB is down) |
| `GET /health/ready` | Readiness — can it serve traffic? | 200 OK (only if SQL Server reachable) |
| `GET /api/auth/me` | Auth check — is session valid? | 200 + user info, or 401 if not authenticated |

### Check Health via Docker

```bash
# All containers
docker compose ps

# Specific service
docker compose ps api

# Live logs
docker compose logs -f api

# Health history
docker compose events | grep health

# Manual health test
docker compose exec api curl http://localhost:8080/health/ready
```

### Expected State

```bash
docker compose ps

NAME               STATUS              PORTS
webhook-api-1      Up (healthy)        8080/tcp
webhook-stream-worker-1  Up (healthy)   (internal only)
webhook-jobs-worker-1    Up (healthy)   (internal only)
webhook-frontend-1 Up (healthy)        8088/tcp
webhook-sqlserver-1 Up (healthy)       1433/tcp
webhook-seq-1      Up                  5341/tcp, 5342/tcp
webhook-redis-1    Up                  (internal only)
```

All `api`, `stream-worker`, `jobs-worker`, `frontend`, `sqlserver` should show `Up (healthy)`. `seq` and `redis` show just `Up` (they don't have health checks configured).

### Manual Health Verification

```bash
# From the host machine
curl -v http://localhost:8080/health/live
curl -v http://localhost:8080/health/ready

# Expected:
# HTTP/1.1 200 OK
# Content-Type: application/json
# ...

# Both should return JSON like:
# {
#   "status": "Healthy"
# }
```

---

## Monitoring & Logs

### Structured Logging (SEQ)

All backend events are streamed to Seq for querying and alerting.

**Access SEQ UI:**
- URL: `http://localhost:5342` (localhost-only; use SSH tunnel for remote access)
- No authentication required (local network only)

**Useful Queries:**

```text
# Recent errors
Level = "Error" or Level = "Fatal"

# Webhook receiver latency (StreamWorker processing time)
@Type = "StreamWorkerEvent"

# Token lifecycle
@Message like "Token%"

# Retention cleanup history
@Message like "Retention cleanup%"

# High response times (> 100ms)
DurationMs > 100
```

### Container Logs

```bash
# Tail real-time logs for all services
docker compose logs -f

# Tail specific service
docker compose logs -f api
docker compose logs -f stream-worker
docker compose logs -f jobs-worker

# View last 200 lines
docker compose logs --tail=200 api

# Export logs to file
docker compose logs > logs.txt
```

### Key Log Patterns

| Pattern | Meaning | Action |
|---------|---------|--------|
| `Application started. Listening on` | API ready | Normal startup |
| `The configured password does not match the expected format` | BCrypt hash invalid | Regenerate AUTH_PASSWORD_HASH |
| `One or more validations failed: Webhook:BaseUrl must not be empty` | Missing WEBHOOK_BASE_URL | Set env var and restart |
| `A timeout occurred while waiting for the database to be ready` | SQL Server not responding | Check SQL Server container health |
| `An error occurred during migration: Cannot open database` | Migration failed | Check disk space; restart sqlserver |

---

## Common Issues & Solutions

### SQL Server Container Exits Immediately

**Symptom:** `docker compose logs sqlserver` shows instant exit

**Likely Causes:**
- `SA_PASSWORD` does not meet SQL Server complexity requirements
- Insufficient RAM (< 3 GB available)

**Solution:**

```bash
# Check the error
docker compose logs sqlserver

# Fix SA_PASSWORD in .env:
# - Must be 8+ chars
# - Must contain uppercase, lowercase, number, and symbol
# Example: P@ssw0rd!SafeValue2024

# Restart
docker compose down
docker compose up -d sqlserver
```

### API Cannot Reach Database

**Symptom:** API logs show `SqlException: A network-related or instance-specific error occurred`

**Likely Causes:**
- SQL Server not yet healthy (startup can take 30–45 seconds)
- Database name changed in connection string
- SQL Server container exited

**Solution:**

```bash
# Check SQL Server health
docker compose ps sqlserver

# Wait and retry
docker compose logs sqlserver | tail -20

# If it's been > 45 seconds and still not healthy, restart
docker compose restart sqlserver

# Then restart API
docker compose restart api
```

### API Returns 401 on All Routes

**Symptom:** `curl http://localhost:8088/api/tokens` returns 401 Unauthorized

**Likely Causes:**
- Session cookie not being sent (CORS issue in dev)
- `AUTH_PASSWORD_HASH` is plaintext, not BCrypt hash
- Session revoked on logout

**Solution:**

```bash
# Check if auth is working at all
curl -c cookies.txt -d "username=admin&password=YourPassword" \
  http://localhost:8080/api/auth/login

# Verify AUTH_PASSWORD_HASH is a BCrypt hash (starts with $2)
grep AUTH_PASSWORD_HASH .env

# If plaintext, regenerate
dotnet run --project tools/RotatePassword -- --password "YourPassword123!"

# Update .env and restart
docker compose up -d api
```

### SSE Returns 401 in Dev Mode (Angular at :4200)

**Symptom:** Angular dev server cannot subscribe to SSE; browser console shows 401

**Likely Causes:**
- Dev CORS override not loaded
- `withCredentials` removed from `EventSource` call

**Solution:**

```bash
# Use the dev override compose file
docker compose -f docker-compose.yml -f docker-compose.override.yml up -d

# Or ensure .AllowCredentials() in Program.cs:
# cors.AllowCredentials();

# Verify EventSource in frontend/webhook-spa/src/app/core/services/sse.service.ts:
# new EventSource(url, { withCredentials: true })

# Restart both services
docker compose restart api frontend
```

### Nginx Shows 502 Bad Gateway

**Symptom:** `http://localhost:8088` returns "502 Bad Gateway"

**Likely Causes:**
- API container unhealthy or not started
- Nginx started before API was ready
- API container IP changed (cache mismatch)

**Solution:**

```bash
# Check API health
docker compose ps api

# If not "healthy", check logs
docker compose logs api | tail -50

# Restart the rebuild script
pwsh scripts/rebuild-and-wait.ps1

# Or manually
docker compose restart api
docker compose restart frontend
```

### Retention Cleanup Not Running

**Symptom:** Requests older than RETENTION_DAYS still appear in the UI

**Likely Causes:**
- `jobs-worker` container exited or unhealthy
- Database errors stopping the cleanup loop
- `RetentionCleanupService` not running (runs every 24h)

**Solution:**

```bash
# Check jobs-worker status
docker compose ps jobs-worker

# Check logs for errors
docker compose logs jobs-worker | tail -50

# Verify it's running as a single replica (not scaled)
docker compose config | grep -A 5 "jobs-worker:"

# Manual trigger (run once):
docker compose exec jobs-worker dotnet <command>
# (Note: RetentionCleanupService cannot be triggered manually; waits for next 24h tick)

# To force cleanup, restart the service
docker compose restart jobs-worker
```

### High Memory Usage on SQL Server

**Symptom:** `docker stats` shows sqlserver using > 2 GB RAM

**Likely Causes:**
- Large number of captured requests (> 1 million)
- Buffer pool not limited in config
- Memory leak in application code

**Solution:**

```bash
# Check current memory limit
docker inspect webhook-sqlserver-1 | grep Memory

# Set a memory limit in docker-compose.yml:
# services:
#   sqlserver:
#     mem_limit: 2g

# Or increase if too constrained:
# mem_limit: 3g

# Apply changes
docker compose up -d sqlserver

# Monitor
docker stats sqlserver
```

---

## Rollback Procedures

### Rollback to Previous Version

```bash
# 1. Identify the previous working commit
git log --oneline -10

# 2. Save current state (if needed for investigation)
docker compose logs > logs-before-rollback.txt
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SA_PASSWORD" \
  -Q "BACKUP DATABASE [WebhookDb] TO DISK = '/var/opt/mssql/backup/WebhookDb_rollback.bak'"

# 3. Checkout previous version
git checkout <previous-commit>

# 4. Rebuild and restart
docker compose build
docker compose up -d

# 5. If migrations need to be rolled back, use EF Core:
# (Note: This is rare; migrations are additive and rolled back automatically)
docker compose exec api dotnet ef database update <previous-migration> \
  --project src/WebhookService.Infrastructure \
  --startup-project src/WebhookService.API

# 6. Verify
docker compose ps
curl http://localhost:8080/health/ready
```

### Restore from Database Backup

```bash
# 1. Stop the stack
docker compose down

# 2. Restore the backup
docker compose up -d sqlserver
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SA_PASSWORD" \
  -Q "RESTORE DATABASE [WebhookDb] FROM DISK = '/var/opt/mssql/backup/WebhookDb_before_upgrade.bak' WITH REPLACE"

# 3. Restart all services
docker compose up -d

# 4. Verify
docker compose ps
curl http://localhost:8080/health/ready
```

---

## Maintenance Tasks

### Password Rotation

```bash
# 1. Generate a new BCrypt hash
dotnet run --project tools/RotatePassword -- --password "NewAdminPassword123!"

# 2. Update .env
# AUTH_PASSWORD_HASH=<new-hash>

# 3. Restart the API
docker compose up -d api

# 4. Test login with new password
curl -c cookies.txt -d "username=admin&password=NewAdminPassword123!" \
  http://localhost:8080/api/auth/login

# 5. If in production, rotate regularly (every 90 days recommended)
```

### Backup Creation

```bash
# Manual backup
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SA_PASSWORD" \
  -Q "BACKUP DATABASE [WebhookDb] TO DISK = '/var/opt/mssql/backup/WebhookDb_$(date +%Y%m%d_%H%M%S).bak'"

# Automated backups (cron job on host)
# Add to crontab -e:
# 0 2 * * * docker compose -f /path/to/webhook/docker-compose.yml exec -T sqlserver \
#   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" \
#   -Q "BACKUP DATABASE [WebhookDb] TO DISK = '/var/opt/mssql/backup/WebhookDb_daily_$(date +\%Y\%m\%d).bak'"

# List backups
docker compose exec sqlserver ls -lah /var/opt/mssql/backup/
```

### Data Retention & Cleanup

```bash
# View current retention setting
grep RETENTION_DAYS .env

# Update retention (in days)
# Edit .env: RETENTION_DAYS=30

# Apply immediately (restart triggers next cleanup cycle)
docker compose up -d

# Monitor cleanup via SEQ
# Query: @Message like 'Retention cleanup%'

# Force cleanup immediately (if needed)
# (Note: RetentionCleanupService runs on a 24-hour timer; cannot be manually triggered)
# Workaround: Restart jobs-worker to reset the timer
docker compose restart jobs-worker
```

### SSL Certificate Renewal (if using HTTPS)

If deployed behind a reverse proxy with SSL (e.g., Let's Encrypt):

```bash
# 1. Renew certificate on the proxy (not in this compose stack)
# Proxy should be: https://webhook.example.com → http://localhost:8088

# 2. No changes needed to the Docker Compose stack (HTTP-only internally)

# 3. Verify external access
curl https://webhook.example.com/health/ready
```

### Update Dependencies

```bash
# Check for NuGet updates
dotnet package search --exact

# Check for npm updates
cd frontend/webhook-spa && npm outdated

# Update with care and test thoroughly
dotnet package update
npm update

# Rebuild and test
docker compose build
dotnet test
cd frontend/webhook-spa && npm test
```

---

## Performance Tuning

### Database Query Optimization

The API uses `.AsNoTracking()` on all SELECT queries for performance. If queries are slow:

```bash
# Check SEQ for slow queries (> 100ms)
# Query: DurationMs > 100

# Identify the slow query (logged by LoggingBehavior in MediatR)
# Add an index if needed in EF Core configuration

# Example index (in WebhookRequestConfiguration):
# entity.HasIndex(e => new { e.TokenId, e.ReceivedAt, e.Id })
#   .IsDescending(false, true, true)  // ReceivedAt DESC, Id DESC
#   .HasName("IX_WebhookRequests_TokenId_ReceivedAt_Id");
```

### Memory Cache Tuning

Token cache uses 5-minute sliding expiration:

```bash
# View cache configuration (src/WebhookService.Infrastructure/Redis/RedisTokenCache.cs)
# Current: IMemoryCache with 5-minute sliding expiry

# To tune:
# 1. Increase expiry time (less DB hits, but stale data longer)
# 2. Decrease expiry time (fresher data, more DB hits)

# Edit RedisTokenCache constructor and restart
```

### Request Size Limits

```bash
# Check current limit
grep MAX_REQUEST_SIZE_MB .env

# Update for larger payloads
# Edit .env: MAX_REQUEST_SIZE_MB=20

# Restart API
docker compose up -d api

# Verify
curl -X POST http://localhost:8088/webhook/<token> \
  -d @large-file.bin
```

### SSE Connection Tuning

Max 10 concurrent SSE connections per token (enforced in `SseNotifier`):

```bash
# View the limit (src/WebhookService.Infrastructure/Sse/SseNotifier.cs)
# Current: const int MaxSubscribersPerToken = 10;

# To change:
# 1. Edit SseNotifier.cs
# 2. Rebuild and deploy
# 3. Restart API
```

---

## Emergency Procedures

### Immediate Service Failure

```bash
# 1. Stop everything to prevent data corruption
docker compose down

# 2. Check disk space
df -h

# 3. Check Docker logs
docker system df
docker system logs

# 4. Restart services one by one, checking health
docker compose up -d sqlserver
sleep 45
docker compose up -d redis api stream-worker jobs-worker frontend

# 5. Verify health
docker compose ps
curl http://localhost:8080/health/ready

# 6. If still failing, check for:
# - Corrupted volumes: docker volume inspect <name>
# - Insufficient disk: delete old backups or images
# - Database lock: restart sqlserver with clean volume (destructive!)
```

### Complete Data Loss Recovery

```bash
# If no backup exists, data cannot be recovered.
# To rebuild from scratch:

# 1. Stop and wipe all volumes
docker compose down -v

# 2. Restart with fresh database
docker compose up -d

# 3. All captured requests are gone; UI is empty
```

---

## Getting Help

- **Logs:** `docker compose logs -f <service>`
- **Health:** `docker compose ps` + `curl http://localhost:8080/health/ready`
- **SEQ queries:** Navigate to `http://localhost:5342` and search
- **Troubleshooting:** See README.md §20
- **Architecture:** See CLAUDE.md "Key Non-Obvious Facts"

---

For detailed troubleshooting, see README.md §20 Troubleshooting.
