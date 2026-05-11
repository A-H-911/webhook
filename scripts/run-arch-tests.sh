#!/usr/bin/env bash
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo -e "\033[36m==> .NET architecture tests\033[0m"
dotnet test "$REPO/tests/Hookbin.ArchitectureTests" \
    --configuration Release \
    --logger "console;verbosity=normal"

echo -e "\033[32m==> All architecture checks passed\033[0m"
