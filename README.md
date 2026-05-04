# Webhook Service

A self-hosted webhook inspection and debugging tool. Generate unique URLs, send HTTP requests to them from any source, and inspect every captured request in real time — similar to webhook.site but running entirely on your own infrastructure.

## Features

- **Unique webhook URLs** — create as many token URLs as you need; each is independent
- **Real-time inspection** — live Server-Sent Events push new requests to the UI instantly
- **Full request capture** — method, headers, body, query string, IP address, user agent, size
- **Custom responses** — configure the status code, content type, and body each token returns to callers
- **Search and pagination** — filter captured requests by headers or body content
- **Export** — download any individual request as a JSON file
- **Retention cleanup** — automatically delete requests older than a configurable number of days
- **Structured logging** — all events streamed to SEQ for querying and alerting
- **No authentication** — designed for trusted internal networks; auth can be layered on later

## Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10, ASP.NET Core, Clean Architecture |
| Frontend | Angular (latest), Angular Material |
| Database | SQL Server 2022 |
| Real-time | Server-Sent Events (in-process, no broker) |
| Logging | Serilog → SEQ |
| Container | Docker Compose |

---

## Quick Start

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Docker Engine + Compose v2)

### 1. Clone and configure

```bash
git clone <repo-url>
cd webhook
cp .env.example .env
```

Edit `.env`:

```env
SA_PASSWORD=YourStr0ngP@ssword!
WEBHOOK_BASE_URL=http://your-server-hostname-or-ip
RETENTION_DAYS=7
MAX_REQUEST_SIZE_MB=5
```

`WEBHOOK_BASE_URL` must be the hostname or IP that external callers can reach (e.g. `http://192.168.1.10` or `https://webhooks.example.com`). The service refuses to start if this is not set.

### 2. Start the stack

```bash
docker compose up -d
```

SQL Server takes ~30 s to initialize on first boot. The API retries migrations automatically.

### 3. Open the UI

| Service | URL |
|---------|-----|
| Web UI | http://localhost:8088 |
| API (direct) | http://localhost:8080 |
| SEQ log viewer | http://localhost:5342 |

### 4. Create a webhook URL and send a request

Click **New URL** in the dashboard. A unique URL like `http://your-server/webhook/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` is generated. Send any HTTP request to it:

```bash
curl -X POST "http://localhost:8088/webhook/<token-guid>" \
  -H "Content-Type: application/json" \
  -d '{"event": "order.created", "orderId": 42}'
```

The request appears in the dashboard in real time.

---

## API Reference

### Tokens

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/tokens` | List all active tokens |
| `GET` | `/api/tokens/{id}` | Get a single token |
| `POST` | `/api/tokens` | Create a new token |
| `PUT` | `/api/tokens/{id}` | Update description or active status |
| `DELETE` | `/api/tokens/{id}` | Soft-delete a token |
| `PUT` | `/api/tokens/{id}/custom-response` | Set a custom response for this token |
| `DELETE` | `/api/tokens/{id}/custom-response` | Reset to the default response |

**Create token:**
```json
{ "description": "My integration test hook" }
```

**Set custom response:**
```json
{
  "statusCode": 200,
  "contentType": "application/json",
  "body": "{\"ok\": true}",
  "headers": ""
}
```

### Captured Requests

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/tokens/{tokenId}/requests` | List requests (paginated) |
| `GET` | `/api/tokens/{tokenId}/requests/{id}` | Get a single request with full body |
| `GET` | `/api/tokens/{tokenId}/requests/{id}/export` | Download request as JSON file |
| `DELETE` | `/api/tokens/{tokenId}/requests` | Delete all requests for a token |
| `DELETE` | `/api/tokens/{tokenId}/requests/{id}` | Delete a single request |

**List query parameters:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| `page` | `1` | Page number (1-based) |
| `pageSize` | `20` | Results per page (max 100) |
| `search` | — | Filter by headers or body content |

### Real-Time (SSE)

```
GET /api/tokens/{tokenId}/sse
```

Opens a Server-Sent Events stream. Emits a `new-request` event each time a webhook arrives and a `token-deleted` event when the token is soft-deleted. The initial frame includes `retry: 5000` so browsers reconnect after 5 s on disconnect.

### Webhook Receiver

```
ANY /webhook/{token-guid}
```

Accepts **any HTTP method** and any content type. Returns `200 OK {"message": "Webhook received."}` by default, or the configured custom response. All request data is captured synchronously before responding.

### Health Checks

| Path | Description |
|------|-------------|
| `GET /health/live` | Liveness: always 200 if the process is up |
| `GET /health/ready` | Readiness: checks SQL Server connectivity |

---

## Configuration

All configuration uses environment variables. The API will not start if `WEBHOOK_BASE_URL` is missing or `MaxRequestSizeMb` / `RetentionDays` are out of their valid ranges.

| Variable | Required | Default | Valid Range | Description |
|----------|----------|---------|-------------|-------------|
| `SA_PASSWORD` | Yes | — | — | SQL Server SA password |
| `WEBHOOK_BASE_URL` | Yes | — | Any non-empty string | Base URL for generated webhook URLs |
| `RETENTION_DAYS` | No | `7` | 0–365 | Days to keep requests (0 = keep forever) |
| `MAX_REQUEST_SIZE_MB` | No | `5` | 1–100 | Max accepted request body size |
| `Cors__AllowedOrigins` | No | `""` | Comma-separated origins | Leave empty to disable CORS entirely |

---

## Development

### Backend

**Requirements:** .NET 10 SDK, Docker

```bash
# Start a local SQL Server
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=Dev@12345!" \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

# Run the API (set BaseUrl and connection string in appsettings.Development.json first)
dotnet run --project src/WebhookService.API
```

Useful backend commands:

```bash
dotnet build                            # build entire solution
dotnet format                           # format all C# files
dotnet test tests/WebhookService.UnitTests/  # unit tests only (no Docker needed)

# Apply EF Core migrations manually
dotnet ef database update \
  --project src/WebhookService.Infrastructure \
  --startup-project src/WebhookService.API
```

### Frontend

**Requirements:** Node.js 20+

```bash
cd frontend/webhook-spa
npm install
npm start         # dev server at :4200, proxies /api /webhook /health → :8080
npm run build     # production build
npm test          # unit tests (Vitest)
```

### Full-stack dev with Docker + Angular hot-reload

```bash
docker compose -f docker-compose.yml -f docker-compose.override.yml up -d
```

The override adds `http://localhost:4200` to the API's CORS allowed origins so the Angular dev server can reach the API directly.

---

## Testing

### Unit tests — no dependencies required

```bash
dotnet test tests/WebhookService.UnitTests/
```

Tests cover domain entity invariants and value object behaviour.

### Integration tests — requires Docker

Testcontainers pulls a real SQL Server container automatically. Docker must be running.

```bash
dotnet test tests/WebhookService.IntegrationTests/
```

Covers all API endpoints, pagination, search, IDOR ownership checks, and validation error responses (HTTP 422).

### E2E tests — requires the full stack running

```bash
# Install Playwright browsers (first run only)
pwsh tests/WebhookService.E2ETests/bin/Debug/net10.0/playwright.ps1 install

# Run against the Docker Compose stack
E2E_BASE_URL=http://localhost:8088 dotnet test tests/WebhookService.E2ETests/
```

Covers dashboard load, token creation, real-time appearance in the list, and navigation to the token detail page.

### Run all tests

```bash
dotnet test
```

---

## Project Structure

```
src/
  WebhookService.Domain/           # Entities, value objects, repository interfaces, ISseNotifier
  WebhookService.Application/      # CQRS handlers (MediatR), DTOs, FluentValidation, pipeline behaviors
  WebhookService.Infrastructure/   # EF Core (SQL Server), SseNotifier, RetentionCleanupService
  WebhookService.API/              # Controllers, middleware, Program.cs

frontend/webhook-spa/              # Angular standalone components, Angular Material

tests/
  WebhookService.UnitTests/        # xUnit, domain tests, no infrastructure
  WebhookService.IntegrationTests/ # xUnit, Testcontainers.MsSql, WebApplicationFactory
  WebhookService.E2ETests/         # Playwright, headless Chromium

docker/
  sqlserver/                       # SQL Server image with polling entrypoint + init.sql
  frontend/                        # Nginx multi-stage Dockerfile + nginx.conf
```

### Architecture

The backend follows **Clean Architecture** — dependencies point inward only:

```
API  ──▶  Application  ──▶  Domain
Infrastructure  ──────────▶  Domain
```

- **Domain** — pure C# entities and interfaces, no framework dependencies
- **Application** — CQRS with MediatR; each command/query has its own handler; validation runs as a pipeline behavior (returns HTTP 422 on failure)
- **Infrastructure** — EF Core DbContext, repository implementations, in-process SSE notifier, retention background service
- **API** — thin controllers mapping HTTP to MediatR; global exception middleware; health checks

### SSE architecture

Real-time push is handled in-process using `ConcurrentDictionary<Guid, List<Channel<SseEvent>>>`. Each SSE connection is a bounded channel (capacity 100, drop-oldest strategy). No Redis or external message broker is required. Maximum 10 concurrent SSE connections per token; additional connections receive HTTP 429.

---

## Docker Services

| Service | Port(s) | Notes |
|---------|---------|-------|
| `frontend` | `8088→80` | Nginx serves Angular SPA; reverse-proxies `/api`, `/webhook`, `/health` to the API container |
| `api` | `8080→8080` | ASP.NET Core; applies EF migrations on startup with exponential-backoff retry |
| `sqlserver` | `1433→1433` | SQL Server 2022 Developer edition; polling entrypoint runs `init.sql` once ready |
| `seq` | `5341` (ingest), `5342→80` (UI) | Structured log viewer; no auth in default config |

Data survives `docker compose down` via named volumes. To wipe all data:

```bash
docker compose down -v
```

---

## Observability

Structured logs are emitted via **Serilog** and forwarded to **SEQ** at http://localhost:5342.

Key log events:

| Event | Level | Properties |
|-------|-------|-----------|
| Webhook received | Information | `TokenId`, `Method`, `Path`, `IpAddress`, `SizeBytes` |
| SSE notification failed | Warning | `TokenId`, exception message |
| Retention cleanup ran | Information | `DeletedCount`, `CutoffDate` |
| MediatR request handled | Information | `RequestType`, `Duration` |
| Validation failure | Warning | `RequestType`, field-level errors |
| Startup migration retry | Warning | attempt number, elapsed time |

---

## Troubleshooting

**SQL Server container is unhealthy after startup**

SQL Server takes up to 45 s to initialize on first boot. The API retries migrations automatically. Check logs if it still fails:

```bash
docker compose logs sqlserver
```

**Generated webhook URLs show the wrong hostname**

`WEBHOOK_BASE_URL` must be the address that external callers can reach — not the internal container name. For a machine on your LAN use its LAN IP; behind a reverse proxy use the public hostname.

**SSE does not update in real time**

Verify that Nginx has `proxy_buffering off` on the `~ ^/api/tokens/[^/]+/sse$` location block. If running the Angular dev server at port 4200, SSE connects through `proxy.conf.json` which forwards to port 8080 — ensure the .NET API is running first.

**Integration tests fail with container errors**

Docker must be running before `dotnet test` on the integration project. Testcontainers pulls the SQL Server image automatically on first run, which may take a minute on a slow connection.

**Build errors after pulling updates**

```bash
dotnet restore
cd frontend/webhook-spa && npm install
```
