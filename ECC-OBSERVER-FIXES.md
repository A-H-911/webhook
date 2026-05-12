# ECC Observer & Continuous Learning — Post-Update Fix Checklist

Apply these fixes after every `ecc` plugin update. They solve real bugs discovered
through debugging; without them the observer either silently does nothing or
produces corrupted instinct files on Windows.

---

## 1. Project `.claude/settings.json` — Observer env vars

File: `C:\Users\ahammo\Repos\webhook\.claude\settings.json`

The `env` block must contain these four values. The plugin defaults are too
conservative for Windows and produce silent no-ops without them.

```json
{
  "env": {
    "ECC_OBSERVER_ALLOW_WINDOWS": "true",
    "ECC_OBSERVER_MAX_ANALYSIS_LINES": "500",
    "ECC_OBSERVER_MAX_TURNS": "40",
    "ECC_OBSERVER_TIMEOUT_SECONDS": "480"
  }
}
```

| Key | Default | Fixed value | Why |
|-----|---------|-------------|-----|
| `ECC_OBSERVER_ALLOW_WINDOWS` | `false` | `true` | Observer bails out on Windows unless this is set; nothing runs at all |
| `ECC_OBSERVER_MAX_ANALYSIS_LINES` | 30 | `500` | 30 lines produces too small a context window; Haiku finds no patterns |
| `ECC_OBSERVER_MAX_TURNS` | 20 | `40` | 20 turns is not enough for Haiku to read + write multiple instinct files |
| `ECC_OBSERVER_TIMEOUT_SECONDS` | 120 | `480` | 120s times out before Haiku finishes writing instinct files |

---

## 2. Global `~/.claude/settings.json` — Active-hours gate

The observer has an active-hours guard that skips analysis outside a configured
window. The default window blocks the observer outside business hours. Setting
both to `0` disables the gate entirely (runs 24/7).

```json
{
  "env": {
    "OBSERVER_ACTIVE_HOURS_START": "0",
    "OBSERVER_ACTIVE_HOURS_END": "0",
    "ECC_OBSERVER_ALLOW_WINDOWS": "true"
  }
}
```

Verify with:
```powershell
Select-String "OBSERVER_ACTIVE" "$env:USERPROFILE\.claude\settings.json"
```

---

## 3. `observer-loop.sh` — Windows CWD + prompt-content fix

File: `~/.claude/plugins/cache/everything-claude-code/ecc/<VERSION>/skills/continuous-learning-v2/agents/observer-loop.sh`

Two bugs exist in the `analyze_observations` function around how the claude
subprocess is invoked.

### Bug A — Analysis file not byte-capped (703KB exceeds Read tool limit)

The `tail -n 500` line filter is not enough. Each observation line can be 1–5KB
(it includes full tool input/output JSON). 500 lines of large tool outputs = 703KB,
which exceeds the Claude Read tool's ~256KB limit. Haiku aborts without analyzing
anything.

**Fix:** pipe through `tail -c` after `tail -n` to cap total bytes:

```bash
# WRONG — line count only; can still produce 700KB+ files:
tail -n "$MAX_ANALYSIS_LINES" "$OBSERVATIONS_FILE" > "$analysis_file"

# CORRECT — line count + byte cap:
MAX_ANALYSIS_BYTES="${ECC_OBSERVER_MAX_ANALYSIS_BYTES:-200000}"
tail -n "$MAX_ANALYSIS_LINES" "$OBSERVATIONS_FILE" | tail -c "$MAX_ANALYSIS_BYTES" > "$analysis_file"
```

**The correct default is 15,000 bytes, not 200,000.** Each observation line contains
full tool input/output JSON — a single chrome-devtools screenshot result can be
thousands of tokens. 200KB of JSONL = ~90,000 tokens, which exceeds Haiku's
per-call Read tool limit of ~25,000 tokens. 15,000 bytes ≈ 30 dense lines ≈
10,000–15,000 tokens — safely within the limit.
The log line should also be updated to report the byte cap value for debugging:
```bash
echo "[$(date)] Using last $analysis_count of $obs_count observations for analysis (byte-capped at ${MAX_ANALYSIS_BYTES})" >> "$LOG_FILE"
```

### Bug B — CWD not set before claude invocation

The `analysis_relpath` is a path relative to `$PROJECT_DIR`, but the shell's
working directory when `analyze_observations` runs may not be `$PROJECT_DIR`.
The claude subprocess then cannot resolve the relative path and reads nothing.

**Fix:** add `cd "$PROJECT_DIR"` immediately before the `claude` invocation.

### Bug B — Prompt file path lost after `cd`

On Windows/MSYS2, `mktemp` returns an MSYS-style path (e.g. `/c/Users/...`).
After `cd "$PROJECT_DIR"`, this path may no longer resolve from the new CWD.

**Fix:** read the prompt content into a shell variable *before* the `cd`, then
delete the temp file. Pass the in-memory string via `-p "$prompt_content"`.

### What the fixed block looks like

Locate the section in `analyze_observations` that starts with `prompt_file="$(mktemp ..."`.
It should end up looking like this:

```bash
  prompt_file="$(mktemp "${observer_tmp_dir}/ecc-observer-prompt.XXXXXX")"
  cat > "$prompt_file" <<PROMPT
... (prompt body unchanged) ...
PROMPT

  # FIX B: load content before cd so MSYS path stays valid
  prompt_content="$(cat "$prompt_file" 2>/dev/null || true)"
  rm -f "$prompt_file"
  if [ -z "$prompt_content" ]; then
    echo "[$(date)] Failed to load observer prompt content, skipping analysis" >> "$LOG_FILE"
    rm -f "$analysis_file"
    return
  fi

  # ... timeout / max_turns validation unchanged ...

  # FIX A: set CWD so analysis_relpath resolves correctly
  cd "$PROJECT_DIR" || { echo "[$(date)] Failed to cd to PROJECT_DIR ($PROJECT_DIR), skipping analysis" >> "$LOG_FILE"; rm -f "$analysis_file"; return; }

  # FIX B: use in-memory prompt_content, not the temp file path
  ECC_SKIP_OBSERVE=1 ECC_HOOK_PROFILE=minimal claude --model haiku --max-turns "$max_turns" --print \
    --allowedTools "Read,Write" \
    -p "$prompt_content" >> "$LOG_FILE" 2>&1 &
```

---

## 4. `observer-loop.sh` — ECC_HOOK_PROFILE must be `minimal`, not `observer`

In the same claude invocation line (see fix above), ensure the hook profile is
`minimal`, not `observer`:

```bash
# WRONG — silently falls back to 'standard'; all hooks fire; observer dialogue
# gets recorded as new observations, poisoning the analysis window:
ECC_HOOK_PROFILE=observer claude ...

# CORRECT:
ECC_HOOK_PROFILE=minimal claude ...
```

Root cause: `hook-flags.js` only accepts a hard-coded set of valid profiles.
`observer` is not in that set, so it silently downgrades to `standard`, which
fires all hooks including the observation recorder, creating a feedback loop.

---

## 5. Project guard hook — must use absolute path

File: `C:\Users\ahammo\Repos\webhook\.claude\settings.json`

The `PreToolUse` hook for `guard.js` must use an absolute path. Relative paths
work for `Bash` tool invocations (CWD = project root) but fail for `PowerShell`
invocations on Windows where Claude Code sets CWD to the user home directory.

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash|PowerShell",
        "hooks": [
          {
            "type": "command",
            "command": "node \"C:\\Users\\ahammo\\Repos\\webhook\\.claude\\scripts\\guard.js\""
          }
        ]
      }
    ]
  }
}
```

Error symptom when this is missing:
```
PreToolUse:PowerShell hook error
Failed with non-blocking status code: node:internal/modules/cjs/loader:1505
```

---

## 6. ECC_DISABLED_HOOKS — observer subprocess isolation

When the observer spawns a Haiku subprocess, that subprocess must not fire
session-start/stop hooks or the observation recorder. The complete disable list:

```
ECC_DISABLED_HOOKS=session:start,stop:session-end,stop:evaluate-session,stop:cost-tracker,stop:desktop-notify,stop:format-typecheck,stop:check-console-log,post:session-activity-tracker,pre:edit-write:gateguard-fact-force
```

This is enforced inside `observer-loop.sh` via `ECC_SKIP_OBSERVE=1` (suppresses
the observation recorder) combined with `ECC_HOOK_PROFILE=minimal` (suppresses
most other hooks). If a future plugin version adds proper `ECC_HOOK_PROFILE=observer`
support, that can replace `minimal` + `ECC_SKIP_OBSERVE=1`.

---

## 7. GateGuard — disable for bulk instinct writes

When running manual observer analyses or `/evolve --generate`, GateGuard blocks
every new file write with a 4-fact challenge. Bypass for the duration:

```bash
export ECC_GATEGUARD=off
# or selectively:
export ECC_DISABLED_HOOKS=pre:edit-write:gateguard-fact-force
```

---

## 8. `/instinct-status` — must run via PowerShell, not Bash

The CLI renders confidence bars using Unicode block characters (`█░`). The
Windows default terminal encoding (CP1252) cannot encode them, causing a crash
when invoked from Bash.

```powershell
# Always run this way on Windows:
$env:PYTHONIOENCODING = "utf-8"
python3 "C:/Users/ahammo/.claude/plugins/cache/everything-claude-code/ecc/<VERSION>/skills/continuous-learning-v2/scripts/instinct-cli.py" status
```

---

## 9. Verifying the observer is healthy after a plugin update

```powershell
# 1. Observation count is growing
(Get-Content "C:/Users/ahammo/.local/share/ecc-homunculus/projects/71c119d3d60c/observations.jsonl" | Measure-Object -Line).Lines

# 2. Last observation timestamp
Get-Content "C:/Users/ahammo/.local/share/ecc-homunculus/projects/71c119d3d60c/observations.jsonl" -Tail 1 |
  ConvertFrom-Json | Select-Object timestamp, event, tool

# 3. Instinct status renders without error
$env:PYTHONIOENCODING = "utf-8"
python3 "C:/Users/ahammo/.claude/plugins/cache/everything-claude-code/ecc/<VERSION>/skills/continuous-learning-v2/scripts/instinct-cli.py" status
```

If observations are not growing, `ECC_SKIP_OBSERVE` may be set in your shell
session, or the observer daemon exited. Restart via ECC's start-observer command.

---

## 11. Observer daemon is disabled by default — must enable persistently

The plugin ships with `config.json` set to `"enabled": false`. The `start-observer.sh`
checks this flag and refuses to start when false. **No hook auto-starts the observer**
either — it is always a manual step.

The plugin's own `config.json` is overwritten on every plugin update. Instead, create
a persistent override at `~/.local/share/ecc-homunculus/config.json` — `start-observer.sh`
checks this path first.

```json
// Create: ~/.local/share/ecc-homunculus/config.json
{
  "version": "2.1",
  "observer": {
    "enabled": true,
    "run_interval_minutes": 5,
    "min_observations_to_analyze": 20
  }
}
```

Then start the daemon from the project root:
```bash
cd /path/to/project
bash "~/.claude/plugins/cache/everything-claude-code/ecc/<VERSION>/skills/continuous-learning-v2/agents/start-observer.sh"
```

To restart after editing `observer-loop.sh`:
```bash
bash start-observer.sh stop && bash start-observer.sh
```

To trigger an immediate analysis (instead of waiting 5 min):
```bash
kill -USR1 $(cat ~/.local/share/ecc-homunculus/projects/<PROJECT_ID>/.observer.pid)
```

To re-inject archived observations (e.g. after a failed analysis run archived them prematurely):
```bash
cat ~/.local/share/ecc-homunculus/projects/<PROJECT_ID>/observations.archive/processed-*.jsonl \
  >> ~/.local/share/ecc-homunculus/projects/<PROJECT_ID>/observations.jsonl
```

---

## Quick re-apply checklist after plugin update

1. Verify `webhook/.claude/settings.json` still has the 4 `ECC_OBSERVER_*` env vars — see §1
2. Verify `~/.claude/settings.json` has `OBSERVER_ACTIVE_HOURS_START=0` and `OBSERVER_ACTIVE_HOURS_END=0` — see §2
3. Patch new `observer-loop.sh`: add byte-cap after tail-n line, add `cd "$PROJECT_DIR"`, load `prompt_content` before cd, use `-p "$prompt_content"`, set `ECC_HOOK_PROFILE=minimal` — see §3–4
4. Verify guard hook command is still the absolute path — see §5
5. Confirm `~/.local/share/ecc-homunculus/config.json` still has `enabled: true` — see §11
6. Start the daemon: `bash start-observer.sh` from project root — see §11
7. Run `/instinct-status` via PowerShell — see §8

---

## 10. Known: `Global instincts: 0` when all globals are shadowed by project scope

**This is correct behavior, not a bug.**

`load_all_instincts` deduplicates globals against project-scoped instincts by ID
(lines 438–441 of `instinct-cli.py`). If you promote an instinct that already
exists at project scope with the same ID, the global is silently dropped from the
display — project scope wins.

The global files ARE written to `~/.local/share/ecc-homunculus/instincts/personal/`
and ARE active. They will appear as `[global]` in any other project that does not
have a project-scoped instinct with the same ID.

To confirm the globals are stored correctly:
```powershell
ls "C:/Users/ahammo/.local/share/ecc-homunculus/instincts/personal/"
```

Expected output after promoting the 5 instincts from this project:
```
command-handler-state-mutation-sequence.yaml
command-validator-property-sync.yaml
dotnet-test-filter-targeted.yaml
dto-mapper-consistency.yaml
redis-multi-service-infrastructure-coordination.yaml
```
