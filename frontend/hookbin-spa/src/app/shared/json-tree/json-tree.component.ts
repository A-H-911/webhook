import { ChangeDetectionStrategy, Component, computed, effect, input, signal } from '@angular/core';

type JsonValue = unknown;

interface RenderRow {
  path: string;
  depth: number;
  keyLabel: string | null;
  trailingComma: boolean;
  kind: 'open' | 'close' | 'scalar' | 'collapsed';
  brace?: '{' | '}' | '[' | ']';
  preview?: string;
  scalar?: { type: 'str' | 'num' | 'bool' | 'null'; text: string };
  toggleable: boolean;
}

const AUTO_COLLAPSE_ARRAY_LEN = 20;
const AUTO_COLLAPSE_OBJECT_KEYS = 30;
const MAX_DEPTH = 50;

@Component({
  selector: 'app-json-tree',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="json-tree" role="tree">
      @for (row of visibleRows(); track row.path) {
        <div
          class="json-row"
          [class.json-row--match]="matchedPaths().has(row.path)"
          [style.padding-left.px]="8 + row.depth * 16"
        >
          @if (row.toggleable && row.kind === 'open') {
            <button
              type="button"
              class="json-toggle"
              [attr.aria-expanded]="!isCollapsed(row.path)"
              (click)="toggle(row.path)"
            >
              {{ isCollapsed(row.path) ? '▸' : '▾' }}
            </button>
          } @else {
            <span class="json-toggle-spacer"></span>
          }

          @if (row.keyLabel !== null) {
            <span class="key">"{{ row.keyLabel }}"</span><span class="punct">: </span>
          }

          @switch (row.kind) {
            @case ('open') {
              <span class="punct">{{ row.brace }}</span>
              @if (isCollapsed(row.path) && row.preview) {
                <span class="punct"> {{ row.preview }} </span>
                <span class="punct">{{ row.brace === '{' ? '}' : ']' }}</span>
                @if (row.trailingComma) {
                  <span class="punct">,</span>
                }
              }
            }
            @case ('close') {
              <span class="punct">{{ row.brace }}</span>
              @if (row.trailingComma) {
                <span class="punct">,</span>
              }
            }
            @case ('scalar') {
              <span [class]="row.scalar!.type">{{ row.scalar!.text }}</span>
              @if (row.trailingComma) {
                <span class="punct">,</span>
              }
            }
            @case ('collapsed') {
              <span class="punct">…</span>
            }
          }
        </div>
      }
    </div>
  `,
  styleUrl: './json-tree.component.scss',
})
export class JsonTreeComponent {
  value = input.required<JsonValue>();
  filter = input<string>('');
  autoExpandTo = input<string>('');

  private collapsed = signal<Set<string>>(new Set());

  constructor() {
    effect(() => {
      const v = this.value();
      const expandTo = this.autoExpandTo();
      const initial = computeInitialCollapsed(v);
      if (expandTo) {
        for (const ancestor of ancestorsOf(expandTo)) initial.delete(ancestor);
      }
      this.collapsed.set(initial);
    });
  }

  private allRows = computed(() => buildRows(this.value()));

  matchedPaths = computed<Set<string>>(() => {
    const needle = this.filter().trim().toLowerCase();
    if (!needle) return new Set();
    return matchPaths(this.allRows(), needle);
  });

  visibleRows = computed<RenderRow[]>(() => {
    const rows = this.allRows();
    const matched = this.matchedPaths();
    const collapsed = this.collapsed();
    const hasFilter = matched.size > 0 || this.filter().trim().length > 0;

    const effectiveCollapsed = new Set(collapsed);
    if (hasFilter) {
      for (const path of matched) {
        for (const ancestor of ancestorsOf(path)) effectiveCollapsed.delete(ancestor);
      }
    }

    const subtreeHasMatch = hasFilter ? computeSubtreeMatches(matched) : null;
    const out: RenderRow[] = [];
    let hideUnder: string | null = null;

    for (const row of rows) {
      if (hideUnder !== null) {
        if (row.path.startsWith(hideUnder + '/') || row.path === hideUnder + '/__close') {
          continue;
        }
        hideUnder = null;
      }

      if (
        hasFilter &&
        subtreeHasMatch &&
        !subtreeHasMatch.has(row.path) &&
        !matched.has(row.path)
      ) {
        continue;
      }

      if (row.kind === 'close') {
        const openPath = row.path.replace(/\/__close$/, '');
        if (effectiveCollapsed.has(openPath)) continue;
      }

      out.push(row);

      if (row.kind === 'open' && effectiveCollapsed.has(row.path)) {
        hideUnder = row.path;
      }
    }

    return out;
  });

  isCollapsed(path: string): boolean {
    return this.collapsed().has(path);
  }

  toggle(path: string): void {
    const next = new Set(this.collapsed());
    if (next.has(path)) next.delete(path);
    else next.add(path);
    this.collapsed.set(next);
  }
}

// ── Helpers ────────────────────────────────────────────────────────────────────

function buildRows(value: JsonValue): RenderRow[] {
  const rows: RenderRow[] = [];
  walk(value, '', null, 0, false, rows);
  return rows;
}

function walk(
  value: JsonValue,
  path: string,
  keyLabel: string | null,
  depth: number,
  trailingComma: boolean,
  out: RenderRow[],
): void {
  if (depth > MAX_DEPTH) {
    out.push({
      path,
      depth,
      keyLabel,
      trailingComma,
      kind: 'collapsed',
      toggleable: false,
    });
    return;
  }

  if (value === null) {
    out.push({
      path,
      depth,
      keyLabel,
      trailingComma,
      kind: 'scalar',
      scalar: { type: 'null', text: 'null' },
      toggleable: false,
    });
    return;
  }

  if (Array.isArray(value)) {
    if (value.length === 0) {
      out.push({
        path,
        depth,
        keyLabel,
        trailingComma,
        kind: 'scalar',
        scalar: { type: 'null', text: '[]' },
        toggleable: false,
      });
      return;
    }
    out.push({
      path,
      depth,
      keyLabel,
      trailingComma: false,
      kind: 'open',
      brace: '[',
      preview: `... ${value.length} items`,
      toggleable: true,
    });
    value.forEach((item, idx) => {
      const childPath = `${path}/${idx}`;
      const isLast = idx === value.length - 1;
      walk(item, childPath, null, depth + 1, !isLast, out);
    });
    out.push({
      path: `${path}/__close`,
      depth,
      keyLabel: null,
      trailingComma,
      kind: 'close',
      brace: ']',
      toggleable: false,
    });
    return;
  }

  if (typeof value === 'object') {
    const entries = Object.entries(value as Record<string, JsonValue>);
    if (entries.length === 0) {
      out.push({
        path,
        depth,
        keyLabel,
        trailingComma,
        kind: 'scalar',
        scalar: { type: 'null', text: '{}' },
        toggleable: false,
      });
      return;
    }
    out.push({
      path,
      depth,
      keyLabel,
      trailingComma: false,
      kind: 'open',
      brace: '{',
      preview: `... ${entries.length} ${entries.length === 1 ? 'key' : 'keys'}`,
      toggleable: true,
    });
    entries.forEach(([k, v], idx) => {
      const childPath = `${path}/${escapeKey(k)}`;
      const isLast = idx === entries.length - 1;
      walk(v, childPath, k, depth + 1, !isLast, out);
    });
    out.push({
      path: `${path}/__close`,
      depth,
      keyLabel: null,
      trailingComma,
      kind: 'close',
      brace: '}',
      toggleable: false,
    });
    return;
  }

  if (typeof value === 'string') {
    out.push({
      path,
      depth,
      keyLabel,
      trailingComma,
      kind: 'scalar',
      scalar: { type: 'str', text: `"${value}"` },
      toggleable: false,
    });
    return;
  }

  if (typeof value === 'number') {
    out.push({
      path,
      depth,
      keyLabel,
      trailingComma,
      kind: 'scalar',
      scalar: { type: 'num', text: String(value) },
      toggleable: false,
    });
    return;
  }

  if (typeof value === 'boolean') {
    out.push({
      path,
      depth,
      keyLabel,
      trailingComma,
      kind: 'scalar',
      scalar: { type: 'bool', text: String(value) },
      toggleable: false,
    });
    return;
  }

  out.push({
    path,
    depth,
    keyLabel,
    trailingComma,
    kind: 'scalar',
    scalar: { type: 'null', text: String(value) },
    toggleable: false,
  });
}

function escapeKey(k: string): string {
  return k.replace(/~/g, '~0').replace(/\//g, '~1');
}

function computeInitialCollapsed(value: JsonValue): Set<string> {
  const out = new Set<string>();
  walkForAutoCollapse(value, '', out);
  return out;
}

function walkForAutoCollapse(value: JsonValue, path: string, out: Set<string>): void {
  if (value === null || typeof value !== 'object') return;
  if (Array.isArray(value)) {
    if (value.length > AUTO_COLLAPSE_ARRAY_LEN) out.add(path);
    value.forEach((item, idx) => walkForAutoCollapse(item, `${path}/${idx}`, out));
    return;
  }
  const entries = Object.entries(value as Record<string, JsonValue>);
  if (entries.length > AUTO_COLLAPSE_OBJECT_KEYS) out.add(path);
  for (const [k, v] of entries) walkForAutoCollapse(v, `${path}/${escapeKey(k)}`, out);
}

function ancestorsOf(path: string): string[] {
  const parts = path.split('/').filter((p) => p !== '');
  const out: string[] = [''];
  let acc = '';
  for (let i = 0; i < parts.length - 1; i++) {
    acc += '/' + parts[i];
    out.push(acc);
  }
  return out;
}

function matchPaths(rows: RenderRow[], needle: string): Set<string> {
  const out = new Set<string>();
  for (const row of rows) {
    if (row.kind === 'close') continue;
    const key = row.keyLabel?.toLowerCase() ?? '';
    const val = row.scalar?.text.toLowerCase() ?? '';
    if (key.includes(needle) || val.includes(needle)) out.add(row.path);
  }
  return out;
}

function computeSubtreeMatches(matched: Set<string>): Set<string> {
  const out = new Set<string>();
  for (const m of matched) {
    for (const ancestor of ancestorsOf(m)) out.add(ancestor);
    out.add(m);
  }
  return out;
}
