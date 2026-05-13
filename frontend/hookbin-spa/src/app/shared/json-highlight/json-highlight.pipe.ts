import { Pipe, PipeTransform, inject } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';

/**
 * Tokenize a JSON string and emit syntax-highlighted spans.
 *
 * Security: all source bytes are HTML-escaped before any markup is added,
 * preventing reflected XSS even when the JSON body contains `<script>`-like
 * strings. The post-escape span-wrapped string is then trusted via the
 * sanitizer because we control every character that becomes markup.
 */
@Pipe({
  name: 'jsonHighlight',
  standalone: true,
  pure: true,
})
export class JsonHighlightPipe implements PipeTransform {
  private sanitizer = inject(DomSanitizer);

  transform(value: string | null | undefined): SafeHtml {
    if (!value) return '';
    const escaped = escapeHtml(value);
    const highlighted = tokenize(escaped);
    return this.sanitizer.bypassSecurityTrustHtml(highlighted);
  }
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function tokenize(s: string): string {
  const pattern =
    /(&quot;(?:\\.|(?!&quot;).)*&quot;)\s*(:)?|\b(true|false|null)\b|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)/g;

  return s.replace(pattern, (_match, strLit, colon, kw, num) => {
    if (strLit) {
      if (colon) {
        return `<span class="key">${strLit}</span><span class="punct">:</span>`;
      }
      return `<span class="str">${strLit}</span>`;
    }
    if (kw === 'true' || kw === 'false') return `<span class="bool">${kw}</span>`;
    if (kw === 'null') return `<span class="null">${kw}</span>`;
    if (num !== undefined) return `<span class="num">${num}</span>`;
    return _match;
  });
}
