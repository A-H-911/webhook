import { SecurityContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DomSanitizer } from '@angular/platform-browser';
import { JsonHighlightPipe } from './json-highlight.pipe';

describe('JsonHighlightPipe', () => {
  let pipe: JsonHighlightPipe;
  let sanitizer: DomSanitizer;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    pipe = TestBed.runInInjectionContext(() => new JsonHighlightPipe());
    sanitizer = TestBed.inject(DomSanitizer);
  });

  function toHtml(value: string): string {
    const safe = pipe.transform(value);
    return sanitizer.sanitize(SecurityContext.HTML, safe as any) ?? '';
  }

  it('returns empty string for null/undefined/empty input', () => {
    expect(toHtml('')).toBe('');
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
  });

  it('escapes raw HTML in body content (XSS-safe)', () => {
    const input = '{"x": "<script>alert(1)</script>"}';
    const html = toHtml(input);
    expect(html).not.toContain('<script>');
    expect(html).toContain('&lt;script&gt;');
  });

  it('wraps a string scalar in span.str', () => {
    const html = toHtml('{"a": "hi"}');
    expect(html).toContain('<span class="str">&quot;hi&quot;</span>');
  });

  it('wraps a numeric scalar in span.num', () => {
    const html = toHtml('{"a": 42}');
    expect(html).toContain('<span class="num">42</span>');
  });

  it('wraps booleans and null in their respective classes', () => {
    const html = toHtml('{"a": true, "b": false, "c": null}');
    expect(html).toContain('<span class="bool">true</span>');
    expect(html).toContain('<span class="bool">false</span>');
    expect(html).toContain('<span class="null">null</span>');
  });

  it('wraps object keys in span.key with colon punct', () => {
    const html = toHtml('{"username": "alice"}');
    expect(html).toContain(
      '<span class="key">&quot;username&quot;</span><span class="punct">:</span>',
    );
  });

  it('handles nested objects without breaking spans', () => {
    const html = toHtml('{"a": {"b": 1}}');
    const keys = (html.match(/<span class="key">/g) ?? []).length;
    expect(keys).toBe(2);
  });
});
