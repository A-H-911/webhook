#!/usr/bin/env node
'use strict';

const MAX_STDIN = 1024 * 1024;
let raw = '';

const BLOCKED_PATTERNS = [
  'rm -rf /',
  'drop table',
  'drop database',
  'format c:',
  'del /s /q c:\\',
];

process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => {
  if (raw.length < MAX_STDIN) raw += chunk.substring(0, MAX_STDIN - raw.length);
});

process.stdin.on('end', () => {
  try {
    const parsed = JSON.parse(raw);
    const cmd = (parsed.tool_input && parsed.tool_input.command || '').toLowerCase();
    for (const pattern of BLOCKED_PATTERNS) {
      if (cmd.includes(pattern)) {
        process.stderr.write('[Guard] BLOCKED: destructive pattern detected: ' + pattern + '\n');
        process.stdout.write(raw);
        process.exit(2);
      }
    }
  } catch (_) {
    // non-JSON input — pass through
  }
  process.stdout.write(raw);
});
