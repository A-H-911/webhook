#!/usr/bin/env bash
# Rebuild one or more docker compose services and wait until each .NET service
# reaches /health/ready. Also reloads nginx so it re-resolves the API container IP.
#
# Usage:
#   bash scripts/rebuild-and-wait.sh
#   bash scripts/rebuild-and-wait.sh api
#   bash scripts/rebuild-and-wait.sh api stream-worker
#
# Default services: api stream-worker jobs-worker frontend
# Timeout: REBUILD_TIMEOUT_SECONDS (default 120)

set -euo pipefail

SERVICES=("${@:-api stream-worker jobs-worker frontend}")
if [ "$#" -eq 0 ]; then
    SERVICES=(api stream-worker jobs-worker frontend)
fi
TIMEOUT="${REBUILD_TIMEOUT_SECONDS:-120}"

echo "Rebuilding: ${SERVICES[*]}"
docker compose build "${SERVICES[@]}"
docker compose up -d "${SERVICES[@]}"

DOTNET_SERVICES=(api stream-worker jobs-worker)

for svc in "${SERVICES[@]}"; do
    skip=1
    for ds in "${DOTNET_SERVICES[@]}"; do
        [ "$svc" = "$ds" ] && skip=0 && break
    done
    [ "$skip" -eq 1 ] && continue

    echo "Waiting for $svc to become healthy..."
    deadline=$((SECONDS + TIMEOUT))
    ready=0
    while [ "$SECONDS" -lt "$deadline" ]; do
        if docker compose exec -T "$svc" curl -fsS http://localhost:8080/health/ready >/dev/null 2>&1; then
            ready=1
            break
        fi
        sleep 2
    done

    if [ "$ready" -eq 0 ]; then
        echo "ERROR: Timed out waiting for $svc to reach /health/ready after ${TIMEOUT}s" >&2
        exit 1
    fi
    echo "$svc is healthy"
done

# Reload nginx so it re-resolves the api container IP
if printf '%s\n' "${SERVICES[@]}" | grep -qxE 'frontend|api'; then
    docker compose exec -T frontend nginx -s reload
    echo "nginx reloaded"

    # Verify nginx is serving on the host-side port before returning
    echo "Waiting for nginx to serve on http://localhost:8088 ..."
    deadline=$((SECONDS + TIMEOUT))
    nginx_ready=0
    while [ "$SECONDS" -lt "$deadline" ]; do
        if curl -fsS --max-time 3 http://localhost:8088/ >/dev/null 2>&1; then
            nginx_ready=1
            break
        fi
        sleep 2
    done
    if [ "$nginx_ready" -eq 0 ]; then
        echo "ERROR: nginx not serving on port 8088 after ${TIMEOUT}s" >&2
        exit 1
    fi
    echo "nginx is serving"
fi

echo "All services ready."
