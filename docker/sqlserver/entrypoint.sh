#!/bin/bash
set -e

/opt/mssql/bin/sqlservr &
MSSQL_PID=$!

cleanup() {
    echo "Received shutdown signal, stopping SQL Server..."
    kill -TERM "$MSSQL_PID" 2>/dev/null
    wait "$MSSQL_PID"
}
trap cleanup SIGTERM SIGINT

echo "Waiting for SQL Server to be ready..."
until /opt/mssql-tools18/bin/sqlcmd \
      -S localhost -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" > /dev/null 2>&1; do
    sleep 2
done

echo "Running init.sql..."
/opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C -i /init.sql

echo "Initialisation complete."
wait $MSSQL_PID
