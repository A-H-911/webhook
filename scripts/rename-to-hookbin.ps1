# Compatible with Windows PowerShell 5.1+
<#
.SYNOPSIS
    Atomically renames the Hookbin solution to Hookbin.
.DESCRIPTION
    1. git mv source directories (preserves history)
    2. git mv .csproj / .slnx files (preserves history)
    3. git mv Angular spa folder
    4. Replace all text patterns in file contents
    5. Print summary
.NOTES
    Run from the repo root.  Requires Git and PowerShell 7+.
    After the script: dotnet build  /  dotnet test  /  npm test
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $RepoRoot

function Write-Step([string]$Msg) { Write-Host "`n==> $Msg" -ForegroundColor Cyan }
function Write-Ok([string]$Msg)   { Write-Host "    OK  $Msg" -ForegroundColor Green }
function Write-Chg([string]$Msg)  { Write-Host "    CHG $Msg" -ForegroundColor Yellow }

# --------------------------------------------------------------------------
# Step 1 — git mv source project directories
# --------------------------------------------------------------------------
Write-Step "Renaming src project directories"

$SrcMoves = @(
    @('src/Hookbin.API',            'src/Hookbin.API'),
    @('src/Hookbin.Application',    'src/Hookbin.Application'),
    @('src/Hookbin.Domain',         'src/Hookbin.Domain'),
    @('src/Hookbin.Infrastructure', 'src/Hookbin.Infrastructure'),
    @('src/Hookbin.StreamWorker',   'src/Hookbin.StreamWorker'),
    @('src/Hookbin.JobsWorker',     'src/Hookbin.JobsWorker')
)

foreach ($Pair in $SrcMoves) {
    $From, $To = $Pair
    if (Test-Path $From) {
        git mv $From $To
        Write-Ok "$From  →  $To"
    } elseif (Test-Path $To) {
        Write-Ok "$To (already renamed)"
    } else {
        Write-Warning "Source not found: $From"
    }
}

# --------------------------------------------------------------------------
# Step 2 — git mv test project directories
# --------------------------------------------------------------------------
Write-Step "Renaming test project directories"

$TestMoves = @(
    @('tests/Hookbin.UnitTests',         'tests/Hookbin.UnitTests'),
    @('tests/Hookbin.IntegrationTests',   'tests/Hookbin.IntegrationTests'),
    @('tests/Hookbin.E2ETests',           'tests/Hookbin.E2ETests'),
    @('tests/Hookbin.ArchitectureTests',  'tests/Hookbin.ArchitectureTests')
)

foreach ($Pair in $TestMoves) {
    $From, $To = $Pair
    if (Test-Path $From) {
        git mv $From $To
        Write-Ok "$From  →  $To"
    } elseif (Test-Path $To) {
        Write-Ok "$To (already renamed)"
    } else {
        Write-Warning "Source not found: $From"
    }
}

# --------------------------------------------------------------------------
# Step 3 — git mv Angular SPA folder
# --------------------------------------------------------------------------
Write-Step "Renaming Angular SPA folder"

if (Test-Path 'frontend/hookbin-spa') {
    git mv 'frontend/hookbin-spa' 'frontend/hookbin-spa'
    Write-Ok "frontend/hookbin-spa  →  frontend/hookbin-spa"
} elseif (Test-Path 'frontend/hookbin-spa') {
    Write-Ok "frontend/hookbin-spa (already renamed)"
} else {
    Write-Warning "frontend/hookbin-spa not found"
}

# --------------------------------------------------------------------------
# Step 4 — git mv .csproj files inside renamed directories
# --------------------------------------------------------------------------
Write-Step "Renaming .csproj files"

$AllProjectDirs = @(
    'src/Hookbin.API',
    'src/Hookbin.Application',
    'src/Hookbin.Domain',
    'src/Hookbin.Infrastructure',
    'src/Hookbin.StreamWorker',
    'src/Hookbin.JobsWorker',
    'tests/Hookbin.UnitTests',
    'tests/Hookbin.IntegrationTests',
    'tests/Hookbin.E2ETests',
    'tests/Hookbin.ArchitectureTests'
)

foreach ($Dir in $AllProjectDirs) {
    if (-not (Test-Path $Dir)) { continue }
    $OldCsproj = Get-ChildItem -Path $Dir -Filter 'Hookbin.*.csproj' -ErrorAction SilentlyContinue
    foreach ($File in $OldCsproj) {
        $NewName = $File.Name -replace 'Hookbin\.', 'Hookbin.'
        $NewPath = Join-Path $Dir $NewName
        git mv $File.FullName $NewPath
        Write-Ok "$($File.Name)  →  $NewName"
    }
    $AlreadyRenamed = Get-ChildItem -Path $Dir -Filter 'Hookbin.*.csproj' -ErrorAction SilentlyContinue
    foreach ($File in $AlreadyRenamed) {
        Write-Ok "$($File.Name) (already renamed)"
    }
}

# --------------------------------------------------------------------------
# Step 5 — git mv solution file
# --------------------------------------------------------------------------
Write-Step "Renaming solution file"

if (Test-Path 'Hookbin.slnx') {
    git mv 'Hookbin.slnx' 'Hookbin.slnx'
    Write-Ok "Hookbin.slnx  →  Hookbin.slnx"
} elseif (Test-Path 'Hookbin.slnx') {
    Write-Ok "Hookbin.slnx (already renamed)"
} else {
    Write-Warning "Solution file not found"
}

# --------------------------------------------------------------------------
# Step 6 — Text replacements in file contents
# --------------------------------------------------------------------------
Write-Step "Replacing text patterns in file contents"

# Patterns: ordered most-specific first to avoid double-replacement.
# Each entry: [OldPattern, NewPattern]
$TextReplacements = [ordered]@{
    # Namespace / project name (must come before bare "Webhook" entries)
    'Hookbin'              = 'Hookbin'
    # Docker compose project name
    'hookbin'             = 'hookbin'
    # Angular package name
    'hookbin-spa'                 = 'hookbin-spa'
    # Docker container names (explicit — also caught by project prefix)
    'hookbin-stream-worker'       = 'hookbin-stream-worker'
    'hookbin-jobs-worker'         = 'hookbin-jobs-worker'
    # Docker network name
    'hookbin-net'                 = 'hookbin-net'
    # Options-binding env var prefix (e.g. Hookbin__BaseUrl)
    'Hookbin__'                   = 'Hookbin__'
    # Individual env var names
    'HOOKBIN_BASE_URL'            = 'HOOKBIN_BASE_URL'
    'HOOKBIN_WORKER_ID'           = 'HOOKBIN_WORKER_ID'
    # C# GetSection call (config section name)
    'GetSection("Hookbin")'       = 'GetSection("Hookbin")'
    # JSON config section key (appsettings.json  "Hookbin": { ... })
    '"Hookbin": {'                = '"Hookbin": {'
    # Doc / HTML brand references
    'Hookbin'           = 'Hookbin'
    'Hookbin'             = 'Hookbin'
}

# File extensions to process (text files only)
$Extensions = @(
    '*.cs', '*.csproj', '*.slnx', '*.json', '*.md', '*.yml', '*.yaml',
    '*.html', '*.ts', '*.scss', '*.css',
    'Dockerfile', 'nginx.conf', '.env', '.env.example', '*.sh', '*.ps1',
    '*.txt', '*.config'
)

# Directories to skip entirely
$SkipDirs = @(
    '.git',
    'node_modules',
    'bin',
    'obj',
    'coverage',
    'coverage-results',
    'TestResults',
    '.reports'
)

function Should-Skip([string]$Path) {
    foreach ($Skip in $SkipDirs) {
        if ($Path -match [regex]::Escape("$([IO.Path]::DirectorySeparatorChar)$Skip$([IO.Path]::DirectorySeparatorChar)") -or
            $Path -match [regex]::Escape("$([IO.Path]::DirectorySeparatorChar)$Skip") -and $Path.EndsWith($Skip)) {
            return $true
        }
    }
    return $false
}

$TotalChanged = 0
$Enc = [System.Text.Encoding]::UTF8

# Collect files matching extensions, excluding skip dirs
$FilesToProcess = foreach ($Ext in $Extensions) {
    Get-ChildItem -Path $RepoRoot -Recurse -Filter $Ext -File -ErrorAction SilentlyContinue |
        Where-Object { -not (Should-Skip $_.FullName) }
}
# Also catch extensionless files like Dockerfile
$ExtensionlessFiles = Get-ChildItem -Path $RepoRoot -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -eq '' -and ($_.Name -eq 'Dockerfile' -or $_.Name -eq 'nginx.conf') -and -not (Should-Skip $_.FullName) }

$AllFiles = @($FilesToProcess) + @($ExtensionlessFiles) | Sort-Object FullName -Unique

foreach ($File in $AllFiles) {
    try {
        $Original = [System.IO.File]::ReadAllText($File.FullName, $Enc)
        $Updated  = $Original
        foreach ($Pair in $TextReplacements.GetEnumerator()) {
            $Updated = $Updated.Replace($Pair.Key, $Pair.Value)
        }
        if ($Updated -ne $Original) {
            [System.IO.File]::WriteAllText($File.FullName, $Updated, $Enc)
            Write-Chg $File.FullName.Replace($RepoRoot, '.')
            $TotalChanged++
        }
    } catch {
        Write-Warning "Skipped (binary?): $($File.FullName) — $_"
    }
}

Write-Ok "$TotalChanged file(s) updated"

# --------------------------------------------------------------------------
# Step 7 — Post-rename DANGER ZONE verification (grep-based)
# --------------------------------------------------------------------------
Write-Step "DANGER ZONE verification"

$Checks = @(
    @{
        Pattern = "event: request"
        Must    = $true
        Desc    = "SSE wire event name 'event: request' must remain unchanged"
    },
    @{
        Pattern = 'addEventListener\(''request'''
        Must    = $true
        Desc    = "Angular SseService must still listen for 'request' event"
    },
    @{
        Pattern = 'GetSection\("Hookbin"\)'
        Must    = $false
        Desc    = "No leftover GetSection(Hookbin) calls"
    },
    @{
        Pattern = 'namespace Hookbin'
        Must    = $false
        Desc    = "No leftover Hookbin namespaces in .cs files"
    }
)

$ChecksPassed = $true
foreach ($Check in $Checks) {
    $Hits = Get-ChildItem -Path $RepoRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { -not (Should-Skip $_.FullName) } |
        Where-Object { $_.Extension -in @('.cs','.ts','.json','.yml','.yaml','.md') } |
        Select-String -Pattern $Check.Pattern -ErrorAction SilentlyContinue

    if ($Check.Must) {
        if ($Hits) {
            Write-Ok "$($Check.Desc)"
        } else {
            Write-Warning "FAIL: $($Check.Desc)"
            $ChecksPassed = $false
        }
    } else {
        if (-not $Hits) {
            Write-Ok "$($Check.Desc)"
        } else {
            Write-Warning "WARN: Found remaining matches for '$($Check.Pattern)'"
            $Hits | ForEach-Object { Write-Host "      $($_.Path):$($_.LineNumber): $($_.Line.Trim())" -ForegroundColor DarkYellow }
            $ChecksPassed = $false
        }
    }
}

# --------------------------------------------------------------------------
# Summary
# --------------------------------------------------------------------------
Write-Step "Done"
if ($ChecksPassed) {
    Write-Host "`nAll checks passed. Next steps:" -ForegroundColor Green
} else {
    Write-Host "`nSome checks failed — review warnings above before committing." -ForegroundColor Red
}

Write-Host @"

  1. dotnet build
  2. dotnet test tests/Hookbin.UnitTests/
  3. dotnet test tests/Hookbin.ArchitectureTests/
  4. cd frontend/hookbin-spa && npm install && npm test -- --watch=false
  5. git add -A && git commit -m "feat: rename Hookbin to Hookbin (atomic rebrand)"

"@

Pop-Location
