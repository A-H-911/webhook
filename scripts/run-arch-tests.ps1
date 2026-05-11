#!/usr/bin/env pwsh
# Cross-OS: runs identically on Windows, Linux, macOS under PowerShell 7+.
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

Write-Host "==> .NET architecture tests" -ForegroundColor Cyan
dotnet test (Join-Path $repo "tests/Hookbin.ArchitectureTests") `
    --configuration Release `
    --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> All architecture checks passed" -ForegroundColor Green
